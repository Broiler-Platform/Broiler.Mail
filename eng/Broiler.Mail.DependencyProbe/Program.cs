using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Broiler.Graphics.Android;
using Broiler.Graphics.Linux.OpenGL;
using Broiler.Hosting.Android;
using Broiler.Input.Keyboard.Android;
using Broiler.Input.Text.Android;
using Broiler.Input.Touch.Android;
using Broiler.Mail.Application.Preview;
using Broiler.Mail.Core.Services;
using Broiler.UI;

// Compile against the published APIs. Reading type metadata does not create native surfaces,
// request input permissions, or imply that these backends run on the current OS.
Type[] contracts = [typeof(IUiHost), typeof(IUiClipboardHost), typeof(IUiTextInputHost),
    typeof(ICredentialStore), typeof(IHtmlPreviewHost), typeof(LinuxOpenGlRenderer),
    typeof(AndroidOpenGlEsRenderer), typeof(AndroidTouchProvider), typeof(AndroidTextInputProvider),
    typeof(IAndroidEditorTextSource), typeof(AndroidKeyboardProvider),
    typeof(AndroidUiHost), typeof(AndroidBackendDiagnostics)];

Console.WriteLine(JsonSerializer.Serialize(new
{
    scope = "Published managed API availability only; native host acceptance remains pending.",
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    runtime = RuntimeInformation.FrameworkDescription,
    surfaceFactories = new[] { nameof(LinuxOpenGlRenderer.CreateX11WindowSurface),
        nameof(AndroidOpenGlEsRenderer.CreateWindowSurface) },
    contracts = contracts.Select(type => new
    {
        type = type.FullName,
        assembly = type.Assembly.GetName().Name,
        version = type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    }),
}, new JsonSerializerOptions { WriteIndented = true }));
