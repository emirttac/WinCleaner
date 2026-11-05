using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCleaner.Services;

namespace WinCleaner.ViewModels;

public sealed class SearchSectionViewModel
{
    public SearchSectionViewModel(string navKey, string title, IEnumerable<object> items)
    {
        NavKey = navKey;
        Title = title;
        Items = new ObservableCollection<object>(items);
    }

    public string NavKey { get; }
    public string Title { get; }
    public ObservableCollection<object> Items { get; }
    public int Count => Items.Count;
}

public partial class GlobalSearchViewModel : ObservableObject
{
    private readonly LocalizationService _loc;
    private readonly MainViewModel _main;

    public GlobalSearchViewModel(LocalizationService loc, MainViewModel main)
    {
        _loc = loc;
        _main = main;
        RefreshLabels();
    }

    public ObservableCollection<SearchSectionViewModel> Sections { get; } = new();

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _emptyText = "";
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _hasResults;

    public void RefreshLabels()
    {
        Title = _loc.Get("search.title");
        if (!string.IsNullOrWhiteSpace(Query))
            Rebuild(Query);
        else
            Subtitle = _loc.Get("search.subtitle");
    }

    public void Rebuild(string query)
    {
        Query = query?.Trim() ?? "";
        Sections.Clear();

        if (string.IsNullOrWhiteSpace(Query))
        {
            TotalCount = 0;
            HasResults = false;
            EmptyText = "";
            Subtitle = _loc.Get("search.subtitle");
            return;
        }

        AddSection("presets", _main.Presets.Title, _main.Presets.SearchAll(Query));
        AddSection("gaming", _main.Gaming.Title, _main.Gaming.SearchAll(Query));
        AddSection("telemetry", _main.Telemetry.Title, _main.Telemetry.SearchAll(Query));
        AddSection("bloatware", _main.Bloatware.Title, _main.Bloatware.SearchAll(Query));
        AddSection("visual", _main.Visual.Title, _main.Visual.SearchAll(Query));
        AddSection("win11", _main.Win11Ui.Title, _main.Win11Ui.SearchAll(Query));
        AddSection("services", _main.ServicesPage.Title, _main.ServicesPage.SearchAll(Query));
        AddSection("network", _main.Network.Title, _main.Network.SearchAll(Query));
        AddSection("tools", _main.Tools.Title, _main.Tools.SearchAll(Query));
        AddSection("installer", _main.PackageInstaller.Title, _main.PackageInstaller.SearchAll(Query));

        TotalCount = Sections.Sum(s => s.Count);
        HasResults = TotalCount > 0;
        Subtitle = _loc.Format("search.resultsCount", TotalCount);
        EmptyText = HasResults ? "" : _loc.Format("search.noResults", Query);
    }

    private void AddSection(string navKey, string title, IEnumerable<object> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        Sections.Add(new SearchSectionViewModel(navKey, title, list));
    }

    [RelayCommand]
    private void OpenSection(string? navKey)
    {
        if (string.IsNullOrWhiteSpace(navKey)) return;
        _main.NavigateFromSearch(navKey);
    }
}
