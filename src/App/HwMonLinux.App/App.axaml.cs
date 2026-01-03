using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using System.Linq;
using HwMonLinux.App.Services;
using HwMonLinux.App.ViewModels;
using HwMonLinux.App.Views;
using HwMonLinux.Core;
using HwMonLinux.Providers.Abstractions;
using HwMonLinux.Providers.Infrastructure;
using HwMonLinux.Providers.Providers;
using HwMonLinux.Providers.Services;

namespace HwMonLinux.App;

public partial class App : Application
{
    private SensorCollectorService? _collector;
    private MainWindowViewModel? _mainViewModel;
    private SettingsService? _settingsService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var processRunner = new ProcessRunner(TimeSpan.FromSeconds(3));
            var providers = new ISensorProvider[]
            {
                new LmSensorsJsonProvider(processRunner),
                new SysfsHwmonProvider(),
                new CpuLoadProvider(),
                new CpuFreqProvider(),
                new MemoryInfoProvider(),
                new DiskSmartProvider(processRunner),
                new NvidiaSmiProvider(processRunner),
                new GpuSysfsProvider(),
                new BatteryProvider()
            };

            _settingsService = new SettingsService();
            _settingsService.Load();
            ThemeHelper.ApplyTheme(_settingsService.Settings.ThemeMode);

            _collector = new SensorCollectorService(providers, RefreshOptions.Default);
            _collector.StartAsync().GetAwaiter().GetResult();

            _mainViewModel = new MainWindowViewModel(_collector, _settingsService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_mainViewModel is not null)
        {
            _mainViewModel.Dispose();
        }

        if (_collector is not null)
        {
            await _collector.DisposeAsync();
        }

        _settingsService?.Save();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in toRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
