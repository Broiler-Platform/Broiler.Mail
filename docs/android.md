# Android

Broiler Mail uses the same `Broiler.Hosting.Android` SurfaceView, hardware-accelerated
Canvas, touch/keyboard input, clipboard, and IME as Broiler Browser and Broiler Writer.
Mail's inset layout also reserves space for the soft keyboard. Password fields open a
native password editor with suggestions disabled. The app targets Android 7.0 (API 24) and later, with ARM64 devices
and x64 emulators supported. Its application ID is `org.broiler.mail`.

## Build and install

Install the .NET SDK from `global.json`, the Android workload (`dotnet workload install
android`), Android SDK platform 36, and JDK 21. Visual Studio's .NET Android tooling
can install these prerequisites. Restore uses NuGet.org only.

```powershell
dotnet build Broiler.Android.Mail.slnx -c Debug
adb install -r src/Broiler.Mail.Android/bin/Debug/net10.0-android36.0/org.broiler.mail-Signed.apk
```

For an emulator-only build, add `-p:BroilerAndroidAbis=android-x64`. For a phone-only
build, use `-p:BroilerAndroidAbis=android-arm64`. Do not pass a global RuntimeIdentifiers
property, since shared libraries do not need Android runtime targets.

PowerShell 7 creates a release-configuration APK and SHA-256 checksum:

```powershell
./scripts/Publish-Android.ps1
./scripts/Publish-Android.ps1 -Format aab -Runtime all
```

The script writes `artifacts/Broiler.Mail-<version>-<runtime>.apk` or `.aab` and a
matching `.sha256`. `-Version` and `-VersionCode` override the display version and
Android's numeric version code. These are development-key signed validation builds.
CI creates its own development key, so packages from different runners may not install
over one another. Uninstalling removes local accounts, passwords, and drafts. Store
distribution requires a stable private signing key and increasing version codes using
the standard .NET Android signing properties; no signing secrets are stored here.

CI builds the Android solution and uploads APK/AAB validation artifacts. The
dispatch-only Publish workflow drafts pre-releases containing both Windows and Android
(ARM64 APK and App Bundle) packages.

## Behavior and storage

The shared account setup, IMAP receiving, SMTP sending, plain-text reading/composing,
reply/forward, settings, and draft recovery run on Android. Forms stay on the activity's
single surface. Narrow screens use the existing inbox/list navigation; Android Back
closes a menu or dialog, returns from the reader, and then closes the activity after
the draft is saved. Draft edits autosave, and pausing also requests a flush without
disabling the composer. Android may kill a process without a final lifecycle callback,
so unfinished writes cannot be guaranteed after a forced stop or sudden power loss.

Settings, account profiles, and drafts live in private no-backup application storage.
Passwords are encrypted with AES-GCM using a non-exportable Android Keystore key.
Authenticated data binds each password to its account, protocol, and server settings.
IMAP and SMTP use independent slots; forgetting a password removes its whole slot.
Android backups are disabled. Protocol code continues to require TLS/STARTTLS and
validate certificates.

Saved light/dark preferences, Android's system theme, and font scale apply to the UI.
The Windows-only HTML preview window is unavailable; Android uses the plain-text
preview, including the shared text fallback for HTML-only mail. Background sync and
notifications are not implemented.

## Device acceptance

Use a disposable test account when exercising network operations:

1. Launch in portrait and landscape; check status/navigation bar and keyboard insets.
2. Complete account setup with touch and the soft keyboard. Save a password, restart,
   and test the connection. Change the server and confirm the old password is not used.
3. Configure SMTP separately; receive, open a message, return with Back, reply and send.
4. Edit a draft, switch apps and return, then restart and confirm recovery. Check that
   failed saving or an active send prevents an ordinary Back exit.
5. Check system/light/dark themes, larger system text, clipboard, and a hardware keyboard.
6. Forget both passwords, restart, and confirm they must be entered again.

Shared tests cover phone layouts, single-surface Back navigation, pause/restart draft
recovery, and authenticated credential-file tampering and replacement. Real-device
keyboard variants, Keystore behavior, and provider interoperability still need these
acceptance checks.

Validation on 13 October 2026: Debug x64 APK, Release ARM64 APK, and ARM64/x64 AAB
built successfully; 689 shared tests and 294 Windows tests passed. An API 36 x64
emulator exercised launch, touch scrolling, text input, keyboard insets, native password
entry, Keystore password saving, and password recovery after an app update/restart.
No real mail account was used for this validation.
