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

public partial class StartupRowViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly StartupEntry _entry;
    private readonly Func<IReadOnlyList<IChangeAction>, string, Task>? _previewApply;
    private readonly LocalizationService? _loc;
    private int _gate;

    public StartupRowViewModel(
        StartupEntry entry,
        AppServices app,
        Func<IReadOnlyList<IChangeAction>, string, Task>? previewApply = null,
        LocalizationService? loc = null)
    {
        _entry = entry;
        _app = app;
        _previewApply = previewApply;
        _loc = loc;
        Name = entry.Name;
        CommandText = entry.Command;
        Hive = entry.Hive;
        _isEnabled = entry.IsEnabled;
    }

    public string Name { get; }
    public string CommandText { get; }
    public string Hive { get; }
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (Interlocked.Exchange(ref _gate, 1) == 1) return;

        var desired = !IsEnabled;
        var previous = IsEnabled;
        IsBusy = true;
        IsEnabled = desired;

        try
        {
            var action = new StartupToggleAction(_app.Startup, _entry, desired);
            if (_previewApply is not null)
            {
                IsEnabled = previous;
                await _previewApply([action], _loc?.Get("vis.startup") ?? Name).ConfigureAwait(true);
                var live = _app.Startup.GetStartupEntries()
                    .Any(e => e.Hive == _entry.Hive && e.Name == _entry.Name);
                IsEnabled = live;
                return;
            }

            await OperationHost.TrackAsync(_app.Operations, Name, token =>
                _app.RunActionAsync(Name, action, false, token)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsEnabled = previous;
            MessageBox.Show(ex.Message, "WinCleaner");
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _gate, 0);
        }
    }
}
