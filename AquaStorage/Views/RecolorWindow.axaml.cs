using System;
using AquaStorage.Helpers;
using AquaStorage.Models;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AquaStorage.Views;

public partial class RecolorWindow : Window
{
    public event EventHandler<Color>? Submit;

    public RecolorWindow()
    {
        InitializeComponent();
        var config = ConfigHelper.LoadConfig<SettingsConfig>("Config/SettingsConfig");
        if (config?.AccentColor is { } hex)
        {
            MyColorPicker.SelectedColor = Color.Parse(hex);
        }
    }

    private void SubmitBtn_Click(object? sender, RoutedEventArgs e)
    {
        Submit?.Invoke(this, MyColorPicker.SelectedColor);
        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
