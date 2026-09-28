namespace Broiler.Mail.Core.Settings;

public sealed record ApplicationSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;
    public int WindowWidth { get; init; } = 1100;
    public int WindowHeight { get; init; } = 720;
}

public enum AppTheme { System, Light, Dark }
