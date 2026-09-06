namespace SmartCleaner.Core.Plugins;

public class PluginTargetRule
{
    public string PathTemplate { get; set; } = string.Empty;
    public string Pattern { get; set; } = "*";
    public bool Recursive { get; set; } = true;
    public List<string> Excludes { get; set; } = new();
}

public class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = "Community";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Community";
    public string Icon { get; set; } = "\uE71D";
    public bool IsEnabled { get; set; } = true;
    public List<PluginTargetRule> Rules { get; set; } = new();
}
