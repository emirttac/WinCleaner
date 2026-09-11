using System.Reflection;
using System.Text.Json;

namespace WinCleaner.Data;

public static class CatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<ServiceCatalogEntry> LoadServices() =>
        LoadList<ServiceCatalogEntry>("services.json");

    public static IReadOnlyList<AppCatalogEntry> LoadApps() =>
        LoadList<AppCatalogEntry>("apps.json");

    public static IReadOnlyList<InstallerCatalogEntry> LoadInstallerApps() =>
        LoadList<InstallerCatalogEntry>("apps_installer.json");

    public static IReadOnlyList<TweakCatalogEntry> LoadTweaks() =>
        LoadList<TweakCatalogEntry>("tweaks.json");

    public static IReadOnlyList<PresetCatalogEntry> LoadPresets() =>
        LoadList<PresetCatalogEntry>("presets.json");

    private static IReadOnlyList<T> LoadList<T>(string fileName)
    {
        var assembly = typeof(CatalogLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            throw new FileNotFoundException($"Embedded catalog not found: {fileName}");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Cannot open resource {resourceName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
    }
}
