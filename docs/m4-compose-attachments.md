# Compose attachments

Open **Show attachments** in Compose. Enter the absolute path of a readable, nonempty regular file and select **Add attachment**. A draft accepts up to four files and 8 MiB total. The active and saved drafts share a 16 MiB attachment quota and a 32 MiB draft-store limit. File names and byte counts appear in the picker. Select an entry and choose **Remove selected attachment** to remove it from the editable draft.

Adding a file copies its bytes into the draft. Autosave, parked drafts, profile backup and restore carry the copy. The original path stays out of the saved draft. Editing or deleting the source file after the copy leaves the saved attachment unchanged. Compose shows bytes read and offers **Cancel attachment read**. Send requires a durable submission journal. Use this or a newer build to open drafts or restore backups that use the larger attachment limits.

Messages with attachments use multipart MIME with UTF-8 filenames and base64 file bytes. SMTP encodes attachments in chunks and frames DATA through an 8 KiB buffer. Large uploads display bytes sent. **Cancel send** requests cancellation. Once submission starts, cancellation may leave acceptance uncertain, and Broiler.Mail keeps the draft locked against automatic resend. IMAP Sent copies count the MIME octets before APPEND, then stream those same chunks. The draft keeps the bounded attachment bytes in memory.

The final validation passed 1,636 shared tests and 110 focused Windows tests. Loopback tests cover draft restart after deleting the source file, exact SMTP and IMAP Sent-copy attachment bytes, and private Bcc. NativeAOT package smoke verifies a 4 MiB draft attachment after profile backup and restore.
