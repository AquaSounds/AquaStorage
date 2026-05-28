using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AquaStorage.Helpers;

namespace AquaStorage.Views;

public partial class SettingsWindow : Window
{
    private const string ConfigPathKey = "Config/SettingsConfig";
    private const string DefaultAppFolder = "AquaStorage";

    private readonly List<string> _pathList;

    public event Action<string>? OnPathDeleted;
    public event Action<string>? OnConfigPathChanged;

    public SettingsWindow() : this(new List<string>()) { }

    public SettingsWindow(List<string> paths)
    {
        InitializeComponent();
        _pathList = paths;
        LoadConfigPath();
        RefreshPathList();
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
    }

    private void SaveConfigPath(string path)
    {
        ConfigHelper.SaveConfig(ConfigPathKey, new SettingsConfig { ConfigPath = path });
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

    private void ConfigPathBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        var path = ConfigPathBox.Text?.Trim();
        if (!string.IsNullOrEmpty(path))
            SaveConfigPath(path);
    }

    private void ThemeColorBtn_Click(object? sender, RoutedEventArgs e)
    {
        var recolorWin = new RecolorWindow();
        recolorWin.Submit += (_, color) =>
        {
            App.ApplyAccentColor(color);
            ConfigHelper.SaveConfig("Config/ThemeConfig", new ThemeConfig
            {
                AccentColor = color.ToString()
            });
        };
        recolorWin.ShowDialog(this);
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

public class SettingsConfig
{
    public string? ConfigPath { get; set; }
}
