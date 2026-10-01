namespace SmartCleaner.App.Helpers;

/// <summary>
/// Единый источник версии для UI: читается из сборки, чтобы XAML не дрейфовал относительно csproj.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string VersionBadge { get; } = $"v{Version} FULL SUITE";

    public static string ProductTitle { get; } = $"Puryx — Full Suite v{Version}";
}
