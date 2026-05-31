using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private bool _suppressThemeCombo;
    private bool _suppressFillMode;
    private bool _suppressLanguageChange;

    public event Action<string>? OnPathDeleted;
    public event Action<string>? OnConfigPathChanged;

    public SettingsWindow() : this(new List<string>()) { }

    public SettingsWindow(List<string> paths)
    {
        InitializeComponent();
        _pathList = paths;
        LoadConfigPath();
        RefreshPathList();
        InitThemeCombo();
        InitFillModeCombo();
        LoadAppearanceSettings();

        // Language selector
        _suppressLanguageChange = true;
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-Hans" });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "繁體中文", Tag = "zh-Hant" });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "日本語", Tag = "ja" });

        var config = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey);
        string lang = config?.Language ?? CultureInfo.CurrentUICulture.Name;
        for (int i = 0; i < LanguageCombo.ItemCount; i++)
        {
            if (LanguageCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == lang)
            {
                LanguageCombo.SelectedIndex = i;
                break;
            }
        }
        if (LanguageCombo.SelectedIndex < 0) LanguageCombo.SelectedIndex = 0;
        _suppressLanguageChange = false;
    }

    // ── Theme ComboBox ────────────────────────────────────────────────

    private void InitThemeCombo()
    {
        _suppressThemeCombo = true;
        ThemeCombo.Items.Add(new ComboBoxItem { Content = Localizer.ThemeDark, Tag = "Dark" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = Localizer.ThemeLight, Tag = "Light" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = Localizer.ThemeSystem, Tag = "System" });
        ThemeCombo.SelectedIndex = 2; // default System
        _suppressThemeCombo = false;
    }

    private void ThemeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeCombo) return;
        if (ThemeCombo.SelectedItem is not ComboBoxItem item) return;
        string? mode = item.Tag?.ToString();
        if (string.IsNullOrEmpty(mode)) return;

        App.ApplyTheme(mode);
        var cfg = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();
        cfg.ThemeMode = mode;
        ConfigHelper.SaveConfig(ConfigPathKey, cfg);
    }

    // ── Fill Mode ─────────────────────────────────────────────────────

    private void InitFillModeCombo()
    {
        _suppressFillMode = true;
        FillModeCombo.Items.Add(new ComboBoxItem { Content = Localizer.FillUniformToFill, Tag = "UniformToFill" });
        FillModeCombo.Items.Add(new ComboBoxItem { Content = Localizer.FillStretch, Tag = "Fill" });
        FillModeCombo.Items.Add(new ComboBoxItem { Content = Localizer.FillUniform, Tag = "Uniform" });
        FillModeCombo.Items.Add(new ComboBoxItem { Content = Localizer.FillTile, Tag = "Tile" });
        FillModeCombo.SelectedIndex = 0;
        _suppressFillMode = false;
    }

    private void FillModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressFillMode) return;
        SaveBackgroundConfig();
    }

    // ── Mask / Blur ──────────────────────────────────────────────────

    private void MaskSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        MaskLabel.Text = $"{e.NewValue * 100:F0}%";
        SaveBackgroundConfig();
    }

    private void BlurSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        BlurLabel.Text = $"{e.NewValue * 100:F0}%";
        SaveBackgroundConfig();
    }

    // ── Background Image ──────────────────────────────────────────────

    private async void SelectImageBtn_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.SelectImage,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" } }
            }
        });

        if (files.Count == 0) return;
        string sourcePath = files[0].Path.LocalPath;

        // Copy to cache
        string? cacheName = CacheHelper.SaveBackgroundImage(sourcePath);
        if (cacheName == null) return;

        var cfg = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();
        cfg.BackgroundImagePath = sourcePath;
        cfg.BackgroundImageCache = cacheName;
        ConfigHelper.SaveConfig(ConfigPathKey, cfg);

        App.NotifyBackgroundImageChanged(cacheName);
    }

    private void RemoveImageBtn_Click(object? sender, RoutedEventArgs e)
    {
        var cfg = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();
        cfg.BackgroundImagePath = null;
        cfg.BackgroundImageCache = null;
        ConfigHelper.SaveConfig(ConfigPathKey, cfg);

        App.NotifyBackgroundImageChanged(null);
    }

    private void SaveBackgroundConfig()
    {
        var cfg = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();

        if (FillModeCombo.SelectedItem is ComboBoxItem fillItem)
            cfg.BackgroundFillMode = fillItem.Tag?.ToString() ?? "UniformToFill";

        cfg.BackgroundMask = MaskSlider.Value;
        cfg.BackgroundBlur = BlurSlider.Value;

        ConfigHelper.SaveConfig(ConfigPathKey, cfg);
        App.NotifyBackgroundImageChanged(cfg.BackgroundImageCache);
    }

    // ── Load Initial Values ───────────────────────────────────────────

    private void LoadAppearanceSettings()
    {
        var config = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey);
        if (config == null) return;

        // Theme mode
        string mode = config.ThemeMode;
        if (string.IsNullOrEmpty(mode) && config.IsLightTheme)
            mode = "Light";
        if (string.IsNullOrEmpty(mode))
            mode = "System";

        _suppressThemeCombo = true;
        for (int i = 0; i < ThemeCombo.ItemCount; i++)
        {
            if (ThemeCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == mode)
            {
                ThemeCombo.SelectedIndex = i;
                break;
            }
        }
        _suppressThemeCombo = false;

        // Fill mode
        _suppressFillMode = true;
        string fill = config.BackgroundFillMode;
        for (int i = 0; i < FillModeCombo.ItemCount; i++)
        {
            if (FillModeCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == fill)
            {
                FillModeCombo.SelectedIndex = i;
                break;
            }
        }
        _suppressFillMode = false;

        // Mask / Blur
        MaskSlider.Value = config.BackgroundMask;
        MaskLabel.Text = $"{config.BackgroundMask * 100:F0}%";
        BlurSlider.Value = config.BackgroundBlur;
        BlurLabel.Text = $"{config.BackgroundBlur * 100:F0}%";
    }

    // ── Existing Settings Code (unchanged logic) ──────────────────────

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
            Title = Localizer.Instance["SelectConfigFolder"],
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

    private void LanguageCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange) return;
        if (LanguageCombo.SelectedItem is not ComboBoxItem item) return;
        string? lang = item.Tag?.ToString();
        if (string.IsNullOrEmpty(lang)) return;

        var cfg = ConfigHelper.LoadConfig<SettingsConfig>(ConfigPathKey) ?? new SettingsConfig();
        cfg.Language = lang;
        ConfigHelper.SaveConfig(ConfigPathKey, cfg);

        var dialog = new RestartDialog();
        dialog.ShowDialog(this);
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
