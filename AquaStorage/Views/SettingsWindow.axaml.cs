using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AquaStorage.Helpers;
using AquaStorage.Models;
using AquaStorage.Services;

namespace AquaStorage.Views;

public partial class SettingsWindow : Window
{
    private const string ConfigPathKey = "Config/SettingsConfig";
    private const string DefaultAppFolder = "AquaStorage";

    private readonly List<string> _pathList;
    private bool _suppressThemeToggle;

    public event Action<string>? OnPathDeleted;
    public event Action<string>? OnConfigPathChanged;

    public SettingsWindow() : this(new List<string>()) { }

    private double _listFontSize = CacheService.DefaultFontSize;

    public SettingsWindow(List<string> paths)
    {
        InitializeComponent();
        _pathList = paths;
        LoadConfigPath();
        RefreshPathList();

        _suppressThemeToggle = true;
        var config = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey);
        ThemeToggle.IsChecked = config?.IsLightTheme ?? false;
        _suppressThemeToggle = false;
        ThemeToggle.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleSwitch.IsCheckedProperty && !_suppressThemeToggle)
            {
                bool isLight = ThemeToggle.IsChecked == true;
                App.ApplyTheme(isLight);
                var cfg = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();
                cfg.IsLightTheme = isLight;
                ConfigHelper.SaveConfig(ConfigPathKey, cfg);
            }
        };
    }

    private string GetDefaultConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DefaultAppFolder);
    }

    private void LoadConfigPath()
    {
        var config = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey);
        ConfigPathBox.Text = config?.ConfigPath ?? GetDefaultConfigPath();
        MaxCacheBox.Text = CacheService.MaxCacheBytes is > 0
            ? CacheService.FormatBytes(CacheService.MaxCacheBytes.Value)
            : string.Empty;
        DefaultFontSizeBox.Text = CacheService.DefaultFontSize.ToString("F0");
    }

    private void SaveConfigPath(string path)
    {
        var config = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();
        config.ConfigPath = path;
        ConfigHelper.SaveConfig(ConfigPathKey, config);
        OnConfigPathChanged?.Invoke(path);
    }

    public void RefreshPathList()
    {
        var filter = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            PathListBox.ItemsSource = new List<string>(_pathList);
        }
        else
        {
            PathListBox.ItemsSource = _pathList
                .Where(p => p.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshPathList();
    }

    private void DeleteBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string path)
        {
            OnPathDeleted?.Invoke(path);
            _pathList.Remove(path);
            RefreshPathList();
        }
    }

    private async void BrowseConfigBtn_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select config folder",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            var newPath = folder[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(newPath))
            {
                ConfigPathBox.Text = newPath;
                SaveConfigPath(newPath);
            }
        }
    }

    private void OpenFolderBtn_Click(object? sender, RoutedEventArgs e)
    {
        OpenConfigFolder();
    }

    private void OpenConfigFolder()
    {
        var path = ConfigPathBox.Text?.Trim();
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private void ThemeColorBtn_Click(object? sender, RoutedEventArgs e)
    {
        var recolorWin = new RecolorWindow();
        recolorWin.Submit += (_, color) =>
        {
            App.ApplyAccentColor(color);
            var config = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();
            config.AccentColor = color.ToString();
            ConfigHelper.SaveConfig(ConfigPathKey, config);
        };
        recolorWin.ShowDialog(this);
    }


    private void ClearCacheBtn_Click(object? sender, RoutedEventArgs e)
    {
        CacheService.ClearAll();
    }

    private void MaxCacheBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MaxCacheBox.Text))
            CacheService.SetMaxCache(null);
        else
        {
            var bytes = CacheService.ParseSize(MaxCacheBox.Text);
            if (bytes is > 0) CacheService.SetMaxCache(bytes);
        }
    }

    private void DefaultFontSizeBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (double.TryParse(DefaultFontSizeBox.Text, out double size))
            CacheService.SetDefaultFontSize(size);
    }

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
