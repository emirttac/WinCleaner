using System.Text.Json.Serialization;

namespace WinCleaner.Data;

/// <summary>
/// Full custom profile schema (v2): all applied tweaks + per-service start types.
/// Remains backward-compatible with legacy preset JSON (tweakIds + serviceIds).
/// </summary>
public sealed class CustomProfileDocument
{
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Applied tweak IDs across Gaming, Telemetry, Visual, Network, Tools, etc.</summary>
    [JsonPropertyName("tweakIds")]
    public List<string> TweakIds { get; set; } = new();

    /// <summary>Per-service desired start type (Disabled / Manual / Automatic).</summary>
    [JsonPropertyName("services")]
    public List<ServiceStateEntry> Services { get; set; } = new();

    // --- Legacy PresetCatalogEntry fields (import-only) ---

    [JsonPropertyName("displayNameKey")]
    public string? DisplayNameKey { get; set; }

    [JsonPropertyName("descriptionKey")]
    public string? DescriptionKey { get; set; }

    [JsonPropertyName("serviceIds")]
    public List<string>? ServiceIds { get; set; }

    public string ResolveDisplayName() =>
        !string.IsNullOrWhiteSpace(Name) ? Name
        : !string.IsNullOrWhiteSpace(DisplayNameKey) ? DisplayNameKey!
        : Id;
}

public sealed class ServiceStateEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Disabled | Manual | Automatic</summary>
    [JsonPropertyName("startType")]
    public string StartType { get; set; } = "Disabled";
}
