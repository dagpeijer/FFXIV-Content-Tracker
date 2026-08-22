using System;
using System.Linq;
using System.Reflection;

namespace ContentTracker;

public static class BuildInfo
{
    private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;

    public static string PluginVersion =>
        Assembly.GetName().Version?.ToString(3) ?? "unbekannt";

    public static int DalamudApiLevel =>
        ReadIntMetadata("DalamudApiLevel");

    public static int DataSchemaVersion =>
        ReadIntMetadata("DataSchemaVersion");

    public static string TargetFramework =>
        Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "TargetFramework")?
            .Value ?? "unbekannt";

    private static int ReadIntMetadata(string key)
    {
        var value = Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == key)?
            .Value;

        return int.TryParse(value, out var result) ? result : 0;
    }
}
