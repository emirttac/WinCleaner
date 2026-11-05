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

public partial class PresetRowViewModel : ObservableObject
{
    private readonly PresetCatalogEntry _entry;

    public PresetRowViewModel(PresetCatalogEntry entry, LocalizationService loc)
    {
        _entry = entry;
        Id = entry.Id;
        Refresh(loc);
    }

    public string Id { get; }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";

    public void Refresh(LocalizationService loc)
    {
        Title = loc.Get(_entry.DisplayNameKey);
        Description = loc.Get(_entry.DescriptionKey);
        if (Title == _entry.DisplayNameKey && !_entry.DisplayNameKey.Contains('.'))
            Title = _entry.DisplayNameKey;
    }

    public bool MatchesSearch(string? query) =>
        SearchKeywords.Matches(query, Title, Description, Id);
}
