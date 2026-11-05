using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Compatibility;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Converters;
using WinCleaner.Data;
using WinCleaner.Services;
using WinCleaner.Views;

namespace WinCleaner.ViewModels;

public partial class AppRowViewModel : ObservableObject
{
    private readonly AppCatalogEntry _entry;
    private readonly string _criticalKey;

    public AppRowViewModel(AppCatalogEntry entry, bool isInstalled, LocalizationService loc)
    {
        _entry = entry;
        _criticalKey = "blo.critical";
        Id = entry.Id;
        PackageName = entry.PackageName;
        PackageNames = entry.GetPackageNamePatterns();
        Risk = RiskParser.Parse(entry.Risk);
        IsSystemCritical = entry.SystemCritical;
        IsInstalled = isInstalled;
        CanSelect = !entry.SystemCritical;
        CanReinstall = entry.CanReinstall;
        RefreshLabels(loc);
    }

    public string Id { get; }
    public string PackageName { get; }
    public IReadOnlyList<string> PackageNames { get; }
    public RiskLevel Risk { get; }
    public bool IsSystemCritical { get; }
    public bool IsInstalled { get; }
    public bool CanSelect { get; }
    public bool CanReinstall { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _riskLabel = "";
    [ObservableProperty] private string _criticalLabel = "";
    [ObservableProperty] private string _reinstallHint = "";
    [ObservableProperty] private bool _isSelected;

    public void RefreshLabels(LocalizationService loc)
    {
        Title = loc.Get(_entry.DisplayNameKey);
        Description = loc.Get(_entry.DescriptionKey);
        ReinstallHint = CanReinstall ? loc.Get("blo.canReinstall") : loc.Get("blo.cannotReinstall");
        RiskLabel = Risk switch
        {
            RiskLevel.Blocked => loc.Get("common.risk.blocked"),
            RiskLevel.Dangerous => loc.Get("common.risk.dangerous"),
            RiskLevel.Caution => loc.Get("common.risk.caution"),
            _ => loc.Get("common.risk.safe")
        };
        CriticalLabel = IsSystemCritical ? loc.Get(_criticalKey) : "";
    }

    public bool MatchesQuery(string? query) =>
        SearchKeywords.Matches(query, Title, Description, PackageName, string.Join(' ', PackageNames));
}
