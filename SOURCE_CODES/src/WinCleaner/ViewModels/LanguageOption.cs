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

public partial class LanguageOption : ObservableObject
{
    public LanguageOption(string code, string displayName)
    {
        Code = code;
        _displayName = displayName;
    }

    public string Code { get; }

    [ObservableProperty] private string _displayName;

    public override string ToString() => DisplayName;
}

public partial class ThemeOption : ObservableObject
{
    public ThemeOption(string code, string displayName)
    {
        Code = code;
        _displayName = displayName;
    }

    public string Code { get; }

    [ObservableProperty] private string _displayName;

    public override string ToString() => DisplayName;
}
