using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinCleaner.Core.Services;

/// <summary>
/// Applies visual-effects / window-animation settings the way Windows itself does:
/// registry + UserPreferencesMask + SystemParametersInfo (SPI). Registry alone + Explorer
/// restart does not take effect for the current session (WinUtil also sets UserPreferencesMask).
/// </summary>
public static class VisualEffectsApplier
{
    // WinUtil WPFTweaksDisplay performance mask: ([byte[]](144,18,3,128,16,0,0,0))
    public static readonly byte[] BestPerformanceUserPreferencesMask =
        [0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00];

    private const uint SpiGetAnimation = 0x0048;
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpiSetDragFullWindows = 0x0025;
    private const uint SpiSetMenuAnimation = 0x1003;
    private const uint SpiSetComboBoxAnimation = 0x1005;
    private const uint SpiSetListBoxSmoothScrolling = 0x1007;
    private const uint SpiSetMenuFade = 0x1013;
    private const uint SpiSetSelectionFade = 0x1015;
    private const uint SpiSetToolTipAnimation = 0x1017;
    private const uint SpiSetCursorShadow = 0x101B;
    private const uint SpiSetDropShadow = 0x1025;
    private const uint SpiSetUiEffects = 0x103F;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;
    private const uint SpifFlags = SpifUpdateIniFile | SpifSendChange;

    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = (IntPtr)0xFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint CbSize;
        public int IMinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref AnimationInfo pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    public static void ApplyBestPerformance(RegistryManager registry)
    {
        WriteBestPerformanceRegistry(registry);
        SetUserPreferencesMask(BestPerformanceUserPreferencesMask);
        ApplyLivePerformanceSpi(animationsEnabled: false, dragFullWindows: false, uiEffects: false);
        BroadcastSettingChange();
    }

    public static void ApplyDisableAnimations(RegistryManager registry)
    {
        registry.SetValue("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegistryValueKind.String);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations",
            0,
            RegistryValueKind.DWord);

        // Keep VisualFXSetting as Custom so individual flags stick.
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting",
            3,
            RegistryValueKind.DWord);

        ApplyLivePerformanceSpi(animationsEnabled: false, dragFullWindows: null, uiEffects: null);
        BroadcastSettingChange();
    }

    public static void RevertAnimations(RegistryManager registry)
    {
        registry.SetValue("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "1", RegistryValueKind.String);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations",
            1,
            RegistryValueKind.DWord);

        ApplyLivePerformanceSpi(animationsEnabled: true, dragFullWindows: null, uiEffects: null);
        BroadcastSettingChange();
    }

    public static void RevertBestPerformance(RegistryManager registry, byte[]? previousMask)
    {
        // Restore Windows "Let Windows choose" appearance defaults (VisualFXSetting = 1).
        registry.SetValue("HKCU", @"Control Panel\Desktop", "DragFullWindows", "1", RegistryValueKind.String);
        registry.SetValue("HKCU", @"Control Panel\Desktop", "MenuShowDelay", "400", RegistryValueKind.String);
        registry.SetValue("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "1", RegistryValueKind.String);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "ListviewAlphaSelect",
            1,
            RegistryValueKind.DWord);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "ListviewShadow",
            1,
            RegistryValueKind.DWord);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations",
            1,
            RegistryValueKind.DWord);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting",
            1,
            RegistryValueKind.DWord);
        registry.SetValue("HKCU", @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 1, RegistryValueKind.DWord);

        if (previousMask is { Length: > 0 })
            SetUserPreferencesMask(previousMask);
        else
            DeleteUserPreferencesMask();

        ApplyLivePerformanceSpi(animationsEnabled: true, dragFullWindows: true, uiEffects: true);
        BroadcastSettingChange();
    }

    public static bool IsBestPerformanceApplied(RegistryManager registry)
    {
        var fx = registry.GetValueAsString(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting");
        var minAnimate = registry.GetValueAsString("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate");
        var taskbarAnim = registry.GetValueAsString(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations");

        // Accept either Windows "Best performance" (2) or WinUtil custom (3) with animations off.
        var fxOk = fx is "2" or "3";
        var animOff = minAnimate == "0" && taskbarAnim == "0";
        return fxOk && animOff && !IsWindowAnimationEnabled();
    }

    public static bool IsAnimationsDisabled(RegistryManager registry)
    {
        var minAnimate = registry.GetValueAsString("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate");
        var taskbarAnim = registry.GetValueAsString(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations");
        return minAnimate == "0" && taskbarAnim == "0" && !IsWindowAnimationEnabled();
    }

    public static byte[]? GetUserPreferencesMask()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
            return key?.GetValue("UserPreferencesMask") as byte[];
        }
        catch
        {
            return null;
        }
    }

    public static bool IsWindowAnimationEnabled()
    {
        var info = new AnimationInfo { CbSize = (uint)Marshal.SizeOf<AnimationInfo>() };
        if (!SystemParametersInfo(SpiGetAnimation, info.CbSize, ref info, 0))
            return true; // fail open
        return info.IMinAnimate != 0;
    }

    private static void WriteBestPerformanceRegistry(RegistryManager registry)
    {
        // VisualFXSetting = 2 => Windows "Adjust for best performance"
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting",
            2,
            RegistryValueKind.DWord);

        registry.SetValue("HKCU", @"Control Panel\Desktop", "DragFullWindows", "0", RegistryValueKind.String);
        registry.SetValue("HKCU", @"Control Panel\Desktop", "MenuShowDelay", "200", RegistryValueKind.String);
        registry.SetValue("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegistryValueKind.String);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "ListviewAlphaSelect",
            0,
            RegistryValueKind.DWord);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "ListviewShadow",
            0,
            RegistryValueKind.DWord);
        registry.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations",
            0,
            RegistryValueKind.DWord);
        registry.SetValue("HKCU", @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0, RegistryValueKind.DWord);
    }

    private static void ApplyLivePerformanceSpi(bool animationsEnabled, bool? dragFullWindows, bool? uiEffects)
    {
        var info = new AnimationInfo
        {
            CbSize = (uint)Marshal.SizeOf<AnimationInfo>(),
            IMinAnimate = animationsEnabled ? 1 : 0
        };
        SystemParametersInfo(SpiSetAnimation, info.CbSize, ref info, SpifFlags);

        // uiParam=BOOL variants
        SetUiParamBool(SpiSetMenuAnimation, animationsEnabled);
        SetUiParamBool(SpiSetComboBoxAnimation, animationsEnabled);
        SetUiParamBool(SpiSetListBoxSmoothScrolling, animationsEnabled);
        SetUiParamBool(SpiSetMenuFade, animationsEnabled);
        SetUiParamBool(SpiSetSelectionFade, animationsEnabled);
        SetUiParamBool(SpiSetToolTipAnimation, animationsEnabled);
        SetUiParamBool(SpiSetCursorShadow, animationsEnabled);

        // pvParam=BOOL variants
        SetPvParamBool(SpiSetClientAreaAnimation, animationsEnabled);
        SetPvParamBool(SpiSetDropShadow, animationsEnabled);

        if (dragFullWindows.HasValue)
            SetUiParamBool(SpiSetDragFullWindows, dragFullWindows.Value);

        if (uiEffects.HasValue)
            SetPvParamBool(SpiSetUiEffects, uiEffects.Value);
    }

    private static void SetUiParamBool(uint action, bool enabled) =>
        SystemParametersInfo(action, enabled ? 1u : 0u, IntPtr.Zero, SpifFlags);

    private static void SetPvParamBool(uint action, bool enabled) =>
        SystemParametersInfo(action, 0, enabled ? (IntPtr)1 : IntPtr.Zero, SpifFlags);

    private static void SetUserPreferencesMask(byte[] mask)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true)
            ?? throw new InvalidOperationException("Cannot open HKCU\\Control Panel\\Desktop");
        key.SetValue("UserPreferencesMask", mask, RegistryValueKind.Binary);
    }

    private static void DeleteUserPreferencesMask()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            key?.DeleteValue("UserPreferencesMask", throwOnMissingValue: false);
        }
        catch
        {
            // best-effort
        }
    }

    private static void BroadcastSettingChange()
    {
        try
        {
            SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                "Policy",
                SmtoAbortIfHung,
                5000,
                out _);
            SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                "intl",
                SmtoAbortIfHung,
                5000,
                out _);
        }
        catch
        {
            // best-effort
        }
    }
}
