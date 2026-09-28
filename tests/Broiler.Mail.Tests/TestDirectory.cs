using Broiler.Mail.Core.Accounts;

namespace Broiler.Mail.Tests;

public sealed class TestDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Broiler.Mail.Tests", Guid.NewGuid().ToString("N"));
    public string File(string name) => Path.Combine(Root, name);
    public void Create() => Directory.CreateDirectory(Root);
    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    public static AccountProfile Profile() => new()
    {
        Id = AccountId.New(), DisplayName = "Test account", EmailAddress = "test@example.test",
        IncomingServer = new() { Host = "imap.example.test", Port = 993, UserName = "test@example.test" },
    };
}
