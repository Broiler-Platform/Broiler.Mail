using Broiler.Mail.Windows.Hosting;

namespace Broiler.Mail.Windows.Tests;

[Collection("UI theme")]
public sealed class ProfileSmokeCheckTests
{
    [Fact]
    public Task SyntheticProfileCanBeReopenedWithProtectedCredentialsAndMimeParsing() => ProfileSmokeCheck.RunAsync();

    [Fact]
    public void ActualHtmlBackendCanLayOutAndRasterThePublishedFixture() => HtmlRasterSmokeCheck.Run();
}
