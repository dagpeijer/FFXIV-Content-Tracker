using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.IO;

namespace ContentTracker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public bool ExportOnLogout { get; set; } = true;
    public bool ExportOnPluginDispose { get; set; } = true;
    public bool ExportAfterDuty { get; set; } = false;
    public string ExportDirectory { get; set; } = string.Empty;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        if (string.IsNullOrWhiteSpace(ExportDirectory))
            ExportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FFXIV Content Tracker");
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
