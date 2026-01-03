using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HwMonLinux.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText = $"Version: {Assembly.GetExecutingAssembly().GetName().Version}";
        DataContext = this;
    }

    public string VersionText { get; }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
