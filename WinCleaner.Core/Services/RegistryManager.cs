using Microsoft.Win32;

namespace WinCleaner.Core.Services;

public sealed class RegistryManager
{
    public object? GetValue(string hive, string path, string name)
    {
        using var key = OpenKey(hive, path, writable: false);
        if (key is null) return null;
        // Empty name = default (unnamed) value
        var valueName = string.IsNullOrEmpty(name) ? null : name;
        return key.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
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
        // Default (unnamed) value: pass null so Win32 REG_SZ (Default) is set correctly.
        // Empty-string name alone is unreliable for the classic-context-menu CLSID trick.
        if (string.IsNullOrEmpty(name))
            key.SetValue(null, value ?? "", kind);
        else
            key.SetValue(name, value, kind);
    }

    /// <summary>
    /// Win11 classic context menu: CLSID InprocServer32 must exist with an empty REG_SZ (Default).
    /// </summary>
    public void EnableClassicContextMenu()
    {
        const string clsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        const string inproc = clsid + @"\InprocServer32";

        // Clean any half-written key tree first.
        DeleteKeyTree("HKCU", clsid);

        using var key = Registry.CurrentUser.CreateSubKey(inproc, writable: true)
            ?? throw new InvalidOperationException("Cannot create classic context menu CLSID key.");
        key.SetValue(null, "", RegistryValueKind.String);

        // Verify — key without an explicit (Default) value does not restore the classic menu.
        if (!IsClassicContextMenuEnabled())
            throw new InvalidOperationException("Classic context menu registry value was not written.");
    }

    public bool IsClassicContextMenuEnabled()
    {
        const string inproc = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(inproc, writable: false);
            if (key is null)
                return false;

            // Default value must be present as REG_SZ (empty string is the required payload).
            var kind = key.GetValueKind("");
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                return false;

            var raw = key.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return raw is string;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // GetValueKind throws when (Default) was never written.
            return false;
        }
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
            if (!ValuesEqual(raw.ToString(), expected, "DWord"))
                return false;
        }

        return true;
    }

    /// <summary>Returns true when at least one subkey exists and none contain the named value.</summary>
    public bool AreAllSubkeysMissing(string hive, string path, string name)
    {
        using var parent = OpenKey(hive, path, writable: false);
        if (parent is null) return false;

        var subNames = parent.GetSubKeyNames();
        if (subNames.Length == 0) return false;

        var valueName = NormalizeValueName(name);
        foreach (var subName in subNames)
        {
            using var sub = parent.OpenSubKey(subName, writable: false);
            if (sub is null) return false;
            if (sub.GetValue(valueName, defaultValue: null) is not null)
                return false;
        }

        return true;
    }

    /// <summary>Deletes a named value from every subkey under path. Returns how many deletes succeeded.</summary>
    public int DeleteValueOnAllSubkeys(string hive, string path, string name)
    {
        using var parent = OpenKey(hive, path, writable: true);
        if (parent is null) return 0;

        var count = 0;
        var valueName = NormalizeValueName(name);
        foreach (var subName in parent.GetSubKeyNames())
        {
            using var sub = parent.OpenSubKey(subName, writable: true);
            if (sub is null) continue;
            try
            {
                sub.DeleteValue(valueName, throwOnMissingValue: false);
                count++;
            }
            catch
            {
                // Best-effort delete
            }
        }

        return count;
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

    /// <summary>Compares a live registry string to a catalog value, including DWORD -1 vs 4294967295.</summary>
    public static bool ValuesEqual(string? current, string expected, string valueKind)
    {
        if (current is null)
            return false;
        if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            return true;

        var kind = ParseKind(valueKind);
        if (kind is not RegistryValueKind.DWord and not RegistryValueKind.QWord)
            return false;

        try
        {
            var a = ParseValue(current, kind);
            var b = ParseValue(expected, kind);
            return Convert.ToInt64(a) == Convert.ToInt64(b);
        }
        catch
        {
            return false;
        }
    }

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
