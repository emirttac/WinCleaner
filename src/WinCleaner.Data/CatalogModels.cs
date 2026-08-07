using System.Text.Json.Serialization;

namespace WinCleaner.Data;

public sealed class ServiceCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = "";

    [JsonPropertyName("displayNameKey")]
    public string DisplayNameKey { get; set; } = "";

    [JsonPropertyName("descriptionKey")]
    public string DescriptionKey { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Other";

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "Safe";

    [JsonPropertyName("recommendedStart")]
    public string RecommendedStart { get; set; } = "Disabled";

    [JsonPropertyName("minBuild")]
    public int? MinBuild { get; set; }

    [JsonPropertyName("win11Only")]
    public bool Win11Only { get; set; }
}

public sealed class AppCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = "";

    [JsonPropertyName("displayNameKey")]
    public string DisplayNameKey { get; set; } = "";

    [JsonPropertyName("descriptionKey")]
    public string DescriptionKey { get; set; } = "";

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "Safe";

    [JsonPropertyName("systemCritical")]
    public bool SystemCritical { get; set; }

    [JsonPropertyName("win11Only")]
    public bool Win11Only { get; set; }
}

/// <summary>winget-based software installer catalog entry.</summary>
public sealed class InstallerCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayNameKey")]
    public string DisplayNameKey { get; set; } = "";

    [JsonPropertyName("descriptionKey")]
    public string DescriptionKey { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Tools";

    [JsonPropertyName("categoryKey")]
    public string CategoryKey { get; set; } = "";

    [JsonPropertyName("wingetId")]
    public string WingetId { get; set; } = "";

    /// <summary>Default UI glyph / emoji shown beside the app name.</summary>
    [JsonPropertyName("iconGlyph")]
    public string IconGlyph { get; set; } = "📦";

    /// <summary>Optional stable icon key for future image assets.</summary>
    [JsonPropertyName("iconKey")]
    public string IconKey { get; set; } = "";
}

public sealed class TweakCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayNameKey")]
    public string DisplayNameKey { get; set; } = "";

    [JsonPropertyName("descriptionKey")]
    public string DescriptionKey { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "Safe";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "registry";

    [JsonPropertyName("minBuild")]
    public int? MinBuild { get; set; }

    [JsonPropertyName("win11Only")]
    public bool Win11Only { get; set; }

    /// <summary>
    /// When true, UI must show an anti-cheat risk confirmation before apply
    /// (Vanguard / EAC / BattlEye BSOD / ban risk).
    /// </summary>
    [JsonPropertyName("requiresAntiCheatConfirm")]
    public bool RequiresAntiCheatConfirm { get; set; }

    /// <summary>When true, UI shows a reboot-required message after a successful apply.</summary>
    [JsonPropertyName("requiresReboot")]
    public bool RequiresReboot { get; set; }

    [JsonPropertyName("registry")]
    public RegistryTweakSpec? Registry { get; set; }

    /// <summary>
    /// Build-specific registry paths. Factory selects the first matching variant for the current OS build.
    /// Takes precedence over <see cref="Registry"/> when non-empty.
    /// </summary>
    [JsonPropertyName("registryVariants")]
    public List<RegistryBuildVariant>? RegistryVariants { get; set; }

    /// <summary>
    /// Multi-key registry tweak. When present and non-empty, applied as a single toggle
    /// (all keys on apply, all keys reverted together). Takes precedence over <see cref="Registry"/>.
    /// </summary>
    [JsonPropertyName("registries")]
    public List<RegistryTweakSpec>? Registries { get; set; }

    [JsonPropertyName("service")]
    public ServiceTweakSpec? Service { get; set; }

    [JsonPropertyName("task")]
    public TaskTweakSpec? Task { get; set; }

    [JsonPropertyName("powerPlan")]
    public PowerPlanSpec? PowerPlan { get; set; }

    [JsonPropertyName("command")]
    public CommandSpec? Command { get; set; }
}

public sealed class RegistryBuildVariant
{
    [JsonPropertyName("minBuild")]
    public int? MinBuild { get; set; }

    [JsonPropertyName("maxBuild")]
    public int? MaxBuild { get; set; }

    [JsonPropertyName("registry")]
    public RegistryTweakSpec Registry { get; set; } = new();
}

public sealed class RegistryTweakSpec
{
    [JsonPropertyName("hive")]
    public string Hive { get; set; } = "HKLM";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>Value name. Empty string means the key's default (unnamed) value.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("valueKind")]
    public string ValueKind { get; set; } = "DWord";

    [JsonPropertyName("enabledValue")]
    public string EnabledValue { get; set; } = "1";

    [JsonPropertyName("disabledValue")]
    public string DisabledValue { get; set; } = "0";

    /// <summary>When true, "applied" means disabledValue is set (e.g. turn OFF telemetry).</summary>
    [JsonPropertyName("applyMeansDisable")]
    public bool ApplyMeansDisable { get; set; }

    /// <summary>
    /// When reverting / turning off, delete a registry key tree instead of writing disabledValue.
    /// Used for Win11 classic context-menu CLSID trick.
    /// </summary>
    [JsonPropertyName("deleteKeyOnDisable")]
    public bool DeleteKeyOnDisable { get; set; }

    /// <summary>
    /// Optional parent key to delete on disable (defaults to <see cref="Path"/>).
    /// Classic context menu deletes the CLSID key, not only InprocServer32.
    /// </summary>
    [JsonPropertyName("deleteKeyPath")]
    public string? DeleteKeyPath { get; set; }

    /// <summary>Restart explorer.exe after apply/revert so shell UI picks up the change.</summary>
    [JsonPropertyName("restartExplorer")]
    public bool RestartExplorer { get; set; }

    /// <summary>
    /// When true, write the value on every subkey under Path (e.g. NetBT Interfaces, TCP interfaces).
    /// </summary>
    [JsonPropertyName("applyToAllSubkeys")]
    public bool ApplyToAllSubkeys { get; set; }

    /// <summary>
    /// When reverting, delete the named value instead of writing <see cref="DisabledValue"/>.
    /// Used when the Windows default is "value absent" (e.g. SIUF feedback frequency).
    /// </summary>
    [JsonPropertyName("deleteValueOnDisable")]
    public bool DeleteValueOnDisable { get; set; }
}

public sealed class ServiceTweakSpec
{
    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = "";

    [JsonPropertyName("desiredStart")]
    public string DesiredStart { get; set; } = "Disabled";
}

public sealed class TaskTweakSpec
{
    [JsonPropertyName("taskPath")]
    public string TaskPath { get; set; } = "";

    [JsonPropertyName("disable")]
    public bool Disable { get; set; } = true;
}

public sealed class PowerPlanSpec
{
    [JsonPropertyName("schemeGuid")]
    public string SchemeGuid { get; set; } = "";

    [JsonPropertyName("enableUltimate")]
    public bool EnableUltimate { get; set; }
}

public sealed class CommandSpec
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";

    [JsonPropertyName("revertArguments")]
    public string? RevertArguments { get; set; }

    /// <summary>Require elevated process; fail with admin guidance if not elevated.</summary>
    [JsonPropertyName("requiresAdmin")]
    public bool RequiresAdmin { get; set; }

    /// <summary>
    /// BCD / command state probe key (e.g. disabledynamictick, useplatformclock).
    /// Read via `bcdedit /enum {current}` when fileName is bcdedit.
    /// </summary>
    [JsonPropertyName("detectKey")]
    public string? DetectKey { get; set; }

    /// <summary>equals | present | absent</summary>
    [JsonPropertyName("detectMode")]
    public string DetectMode { get; set; } = "equals";

    /// <summary>Expected value when detectMode is equals (case-insensitive; Yes/true/1 accepted for Yes).</summary>
    [JsonPropertyName("detectValue")]
    public string? DetectValue { get; set; }
}

public sealed class PresetCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayNameKey")]
    public string DisplayNameKey { get; set; } = "";

    [JsonPropertyName("descriptionKey")]
    public string DescriptionKey { get; set; } = "";

    [JsonPropertyName("tweakIds")]
    public List<string> TweakIds { get; set; } = new();

    [JsonPropertyName("serviceIds")]
    public List<string> ServiceIds { get; set; } = new();
}
