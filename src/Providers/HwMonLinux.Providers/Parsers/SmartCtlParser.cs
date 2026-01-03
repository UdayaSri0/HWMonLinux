using System.Text.Json;

namespace HwMonLinux.Providers.Parsers;

internal static class SmartCtlParser
{
    public static SmartCtlStatus? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var deviceName = TryGetString(root, "device", "name") ??
                             TryGetString(root, "device", "info_name") ??
                             TryGetString(root, "device", "protocol");

            var model = TryGetString(root, "model_name") ?? TryGetString(root, "model_family");
            bool? passed = null;
            double? powerOnHours = null;
            double? percentageUsed = null;
            double? powerCycles = null;

            if (root.TryGetProperty("smart_status", out var smartStatus) &&
                smartStatus.TryGetProperty("passed", out var passedElement))
            {
                passed = passedElement.GetBoolean();
            }

            double? temperature = null;
            if (root.TryGetProperty("temperature", out var tempElement) &&
                tempElement.TryGetProperty("current", out var current))
            {
                temperature = current.GetDouble();
            }

            if (root.TryGetProperty("power_on_time", out var powerOn) &&
                powerOn.TryGetProperty("hours", out var hoursElement))
            {
                powerOnHours = hoursElement.GetDouble();
            }

            if (root.TryGetProperty("nvme_smart_health_information_log", out var nvmeLog))
            {
                if (nvmeLog.TryGetProperty("power_on_hours", out var nvmeHours))
                {
                    powerOnHours ??= nvmeHours.GetDouble();
                }

                if (nvmeLog.TryGetProperty("percentage_used", out var usedElement))
                {
                    percentageUsed = usedElement.GetDouble();
                }

                if (nvmeLog.TryGetProperty("power_cycles", out var cyclesElement))
                {
                    powerCycles = cyclesElement.GetDouble();
                }

                if (temperature is null &&
                    nvmeLog.TryGetProperty("temperature", out var nvmeTemp))
                {
                    temperature = nvmeTemp.GetDouble();
                }
            }

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                deviceName = "disk";
            }

            return new SmartCtlStatus(deviceName, model, passed, temperature, powerOnHours, percentageUsed, powerCycles);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var child))
        {
            return child.GetString();
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string firstProperty, string secondProperty)
    {
        if (element.TryGetProperty(firstProperty, out var child) &&
            child.TryGetProperty(secondProperty, out var target))
        {
            return target.GetString();
        }

        return null;
    }
}

internal sealed record SmartCtlStatus(
    string DeviceIdentifier,
    string? Model,
    bool? IsHealthy,
    double? TemperatureCelsius,
    double? PowerOnHours,
    double? PercentageUsed,
    double? PowerCycles);
