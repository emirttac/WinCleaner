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

public partial class VisualViewModel : TweaksCategoryViewModel
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;

    public VisualViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null,
        Func<IReadOnlyList<IChangeAction>, string, Task>? previewApply = null)
        : base("Visual", "vis.title", "vis.subtitle", app, loc, confirm, confirmAntiCheat, previewApply)
    {
        _app = app;
        _loc = loc;
        ReloadStartup();
    }

    public ObservableCollection<StartupRowViewModel> StartupItems { get; } = new();
    [ObservableProperty] private string _startupTitle = "";

    public new void RefreshLabels()
    {
        base.RefreshLabels();
        StartupTitle = _loc.Get("vis.startup");
    }

    [RelayCommand]
    private void ReloadStartup()
    {
        StartupItems.Clear();
        foreach (var e in _app.Startup.GetStartupEntries())
            StartupItems.Add(new StartupRowViewModel(e, _app, _previewApply, _loc));
    }

    [RelayCommand]
    private void OpenPagefile()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "SystemPropertiesAdvanced.exe",
            UseShellExecute = true
        });
    }
}
