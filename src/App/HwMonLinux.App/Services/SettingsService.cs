using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HwMonLinux.App.Models;

namespace HwMonLinux.App.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _serializerOptions;

    public SettingsService()
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HWMonLinux");

        Directory.CreateDirectory(basePath);
        _settingsPath = Path.Combine(basePath, "settings.json");
        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions);
            if (settings is not null)
            {
                Settings = settings;
            }
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Settings, _serializerOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
