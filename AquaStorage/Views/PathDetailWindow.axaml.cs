using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using AquaStorage.Helpers;

namespace AquaStorage.Views;

public partial class PathDetailWindow : Window
{
    private readonly List<string> _pathList;

    public event Action<string>? OnPathAdded;
    public event Action<string>? OnPathDeleted;

    public PathDetailWindow() : this(new List<string>()) { }

    public PathDetailWindow(List<string> paths)
    {
        InitializeComponent();
        _pathList = paths;
        RefreshPathList();
    }

    public void RefreshPathList()
    {
        PathListBox.ItemsSource = new List<string>(_pathList);
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

    private void AddBtn_Click(object? sender, RoutedEventArgs e)
    {
        var newPath = NewPathTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(newPath))
        {
            NewPathTextBox.Watermark = "Please enter a valid path!";
            return;
        }

        if (!Directory.Exists(newPath))
        {
            NewPathTextBox.Watermark = "Path does not exist!";
            return;
        }

        if (!_pathList.Contains(newPath))
        {
            _pathList.Add(newPath);
            RefreshPathList();
            OnPathAdded?.Invoke(newPath);
            NewPathTextBox.Text = string.Empty;
            NewPathTextBox.Watermark = "Enter new path to add";
        }
        else
        {
            NewPathTextBox.Watermark = "Path already exists!";
        }
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

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
