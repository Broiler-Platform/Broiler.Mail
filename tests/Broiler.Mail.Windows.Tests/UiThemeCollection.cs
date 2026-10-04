namespace Broiler.Mail.Windows.Tests;

/// <summary>
/// Classes that build a UI session or show a window run one at a time. A Mail window, or a measurement
/// theme applied to it, sets the process-wide palette that every session built meanwhile starts from,
/// and a window being shown takes the focus from another test's window, which ends its IME composition.
/// </summary>
[CollectionDefinition("UI theme", DisableParallelization = true)]
public sealed class UiThemeCollection;
