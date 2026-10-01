using Broiler.HTML.Image.Compat;

namespace Broiler.Mail.Windows.Preview;

/// <summary>Explicitly bind the shipped managed renderer's compatibility backend.</summary>
internal static class HtmlRuntime
{
    private static readonly Lazy<bool> Registration = new(() =>
    {
        StubCompatBootstrapper.EnsureRegistered();
        return true;
    });

    internal static void Initialize() => _ = Registration.Value;
}
