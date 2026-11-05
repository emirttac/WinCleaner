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

public partial class PresetsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly MainViewModel _main;

    public PresetsViewModel(AppServices app, LocalizationService loc, MainViewModel main)
    {
        _app = app;
        _loc = loc;
        _main = main;
        RefreshLabels();
        Reload();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _customName = "MyProfile";
    public ObservableCollection<PresetRowViewModel> Items { get; } = new();

    private List<PresetRowViewModel> _all = new();

    public void RefreshLabels()
    {
        Title = _loc.Get("pre.title");
        Subtitle = _loc.Get("pre.subtitle");
        foreach (var i in _all) i.Refresh(_loc);
    }

    private void Reload()
    {
        _all = CatalogLoader.LoadPresets().Select(p => new PresetRowViewModel(p, _loc)).ToList();
        Filter(null);
    }

    public void Filter(string? query)
    {
        Items.Clear();
        foreach (var item in SearchAll(query))
            Items.Add(item);
    }

    public IEnumerable<PresetRowViewModel> SearchAll(string? query) =>
        _all.Where(i => i.MatchesSearch(query));

    [RelayCommand]
    private async Task ApplyPresetAsync(PresetRowViewModel? row)
    {
        if (row is null) return;
        if (row.Id == "preset-defaults")
        {
            await _main.UndoAllCommand.ExecuteAsync(null);
            return;
        }

        var preset = CatalogLoader.LoadPresets().First(p => p.Id == row.Id);
        var actions = _app.Presets.BuildPreview(preset);
        await _main.ShowPreviewAndApplyAsync(actions, row.Title);
    }

    [RelayCommand]
    private void ExportCustom()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = $"{CustomName}.json"
        };
        if (dialog.ShowDialog() != true) return;

        var tweakIds = _main.Gaming.GetAppliedTweakIds()
            .Concat(_main.Telemetry.GetAppliedTweakIds())
            .Concat(_main.Visual.GetAppliedTweakIds())
            .Concat(_main.Win11Ui.GetAppliedTweakIds())
            .Concat(_main.Network.GetAppliedTweakIds())
            .Concat(_main.Tools.GetAppliedTweakIds())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var profile = new CustomProfileDocument
        {
            SchemaVersion = CustomProfileDocument.CurrentSchemaVersion,
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = string.IsNullOrWhiteSpace(CustomName) ? "MyProfile" : CustomName.Trim(),
            Description = "WinCleaner full profile export",
            TweakIds = tweakIds,
            Services = _main.ServicesPage.GetExportServiceStates().ToList()
        };

        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        MessageBox.Show(_loc.Get("pre.exported"), "WinCleaner");
    }

    [RelayCommand]
    private async Task ImportCustomAsync()
    {
        var dialog = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var profile = JsonSerializer.Deserialize<CustomProfileDocument>(json);
            if (profile is null) return;

            // Normalize legacy fields into the v2 shape when needed
            if (string.IsNullOrWhiteSpace(profile.Name) && !string.IsNullOrWhiteSpace(profile.DisplayNameKey))
                profile.Name = profile.DisplayNameKey;
            if (string.IsNullOrWhiteSpace(profile.Description) && !string.IsNullOrWhiteSpace(profile.DescriptionKey))
                profile.Description = profile.DescriptionKey;

            var actions = _app.Presets.BuildPreview(profile);
            if (actions.Count == 0)
            {
                MessageBox.Show(_loc.Get("pre.importedEmpty"), "WinCleaner");
                return;
            }

            await _main.ShowPreviewAndApplyAsync(actions, profile.ResolveDisplayName());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WinCleaner");
        }
    }
}
