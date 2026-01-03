using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HwMonLinux.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var window = new AboutWindow();
        await window.ShowDialog(this);
    }
}
