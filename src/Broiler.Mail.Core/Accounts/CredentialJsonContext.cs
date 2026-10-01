using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Accounts;

// Property order, casing, numeric enums and escaping are part of the existing credential hash.
internal sealed record CredentialBinding(string Host, int Port, string UserName,
    TransportSecurity Security, AuthenticationMethod Authentication);

[JsonSerializable(typeof(CredentialBinding))]
internal partial class CredentialJsonContext : JsonSerializerContext;
