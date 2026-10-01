namespace Broiler.Mail.Core.Validation;

/// <summary>Identifies the input responsible for a configuration error without parsing display text.</summary>
public sealed class ConfigurationValidationException(string field, string message) : ArgumentException(message)
{
    public string Field { get; } = field;
}
