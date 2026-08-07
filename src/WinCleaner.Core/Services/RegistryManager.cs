using Microsoft.Win32;

namespace WinCleaner.Core.Services;

public sealed class RegistryManager
{
    public object? GetValue(string hive, string path, string name)
    {
        using var key = OpenKey(hive, path, writable: false);
        if (key is null) return null;
        // Empty name = default (unnamed) value
        return key.GetValue(NormalizeValueName(name), defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public string? GetValueAsString(string hive, string path, string name)
    {
        var value = GetValue(hive, path, name);
        return value?.ToString();
    }

    public bool KeyExists(string hive, string path)
    {
        using var key = OpenKey(hive, path, writable: false);
        return key is not null;
    }

    public void SetValue(string hive, string path, string name, object value, RegistryValueKind kind)
    {
        using var key = OpenKey(hive, path, writable: true, create: true)
            ?? throw new InvalidOperationException($"Cannot open/create registry key {hive}\\{path}");
        key.SetValue(NormalizeValueName(name), value, kind);
    }

    public void DeleteValue(string hive, string path, string name)
    {
        using var key = OpenKey(hive, path, writable: true);
        if (key is null) return;
        try { key.DeleteValue(NormalizeValueName(name), throwOnMissingValue: false); } catch { }
    }

    /// <summary>Deletes a key and all subkeys. Missing keys are ignored.</summary>
    public void DeleteKeyTree(string hive, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            ResolveHive(hive).DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort delete
        }
    }

    /// <summary>
    /// Applies a DWORD/string value to all subkeys under path (used for per-interface TCP tweaks).
    /// </summary>
    public int SetValueOnAllSubkeys(string hive, string path, string name, object value, RegistryValueKind kind)
    {
        using var parent = OpenKey(hive, path, writable: true);
        if (parent is null) return 0;

        var count = 0;
        var valueName = NormalizeValueName(name);
        foreach (var subName in parent.GetSubKeyNames())
        {
            using var sub = parent.OpenSubKey(subName, writable: true);
            if (sub is null) continue;
            sub.SetValue(valueName, value, kind);
            count++;
        }
        return count;
    }

    /// <summary>Returns true when at least one subkey exists and every subkey has the expected value.</summary>
    public bool AreAllSubkeysEqual(string hive, string path, string name, string expected)
    {
        using var parent = OpenKey(hive, path, writable: false);
        if (parent is null) return false;

        var subNames = parent.GetSubKeyNames();
        if (subNames.Length == 0) return false;

        foreach (var subName in subNames)
        {
            using var sub = parent.OpenSubKey(subName, writable: false);
            if (sub is null) return false;
            var raw = sub.GetValue(NormalizeValueName(name), defaultValue: null);
            if (raw is null) return false;
            if (!string.Equals(raw.ToString(), expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static RegistryValueKind ParseKind(string kind) =>
        kind.Equals("String", StringComparison.OrdinalIgnoreCase) ? RegistryValueKind.String :
        kind.Equals("QWord", StringComparison.OrdinalIgnoreCase) ? RegistryValueKind.QWord :
        kind.Equals("ExpandString", StringComparison.OrdinalIgnoreCase) ? RegistryValueKind.ExpandString :
        RegistryValueKind.DWord;

    public static object ParseValue(string raw, RegistryValueKind kind) =>
        kind switch
        {
            RegistryValueKind.DWord => ParseDword(raw),
            RegistryValueKind.QWord => long.TryParse(raw, out var l) ? l : Convert.ToInt64(raw),
            _ => raw ?? ""
        };

    /// <summary>
    /// Accepts signed ints, unsigned (e.g. 4294967295 → 0xFFFFFFFF), and 0x hex forms.
    /// </summary>
    private static object ParseDword(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        raw = raw.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return unchecked((int)Convert.ToUInt32(raw, 16));
        if (uint.TryParse(raw, out var u) && u > int.MaxValue)
            return unchecked((int)u);
        if (int.TryParse(raw, out var i))
            return i;
        return Convert.ToInt32(raw);
    }

    /// <summary>Registry default value uses empty string (not null) in SetValue/GetValue.</summary>
    private static string NormalizeValueName(string? name) => name ?? "";

    private static RegistryKey? OpenKey(string hive, string path, bool writable, bool create = false)
    {
        var root = ResolveHive(hive);
        if (create)
            return root.CreateSubKey(path, writable);
        return root.OpenSubKey(path, writable);
    }

    private static RegistryKey ResolveHive(string hive) =>
        hive.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            _ => throw new ArgumentException($"Unknown hive: {hive}")
        };
}
