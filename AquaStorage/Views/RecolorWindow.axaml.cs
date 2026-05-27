using System;
using AquaStorage.Helpers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AquaStorage.Views;

public partial class RecolorWindow : Window
{
    public event EventHandler<Color>? Submit;

    public RecolorWindow()
    {
        InitializeComponent();
        var config = ConfigHelper.LoadConfig<ThemeConfig>("Config/ThemeConfig");
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
}
