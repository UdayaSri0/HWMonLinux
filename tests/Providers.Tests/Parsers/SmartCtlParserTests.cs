using HwMonLinux.Providers.Parsers;
using Xunit;

namespace HwMonLinux.Providers.Tests.Parsers;

public class SmartCtlParserTests
{
    [Fact]
    public void Parse_ReturnsModelAndStatus()
    {
        var json = """
                   {
                     "model_name": "TestDisk",
                     "device": {
                       "name": "sda"
                     },
                     "power_on_time": {
                       "hours": 123
                     },
                     "nvme_smart_health_information_log": {
                       "percentage_used": 7,
                       "power_cycles": 42
                     },
                     "smart_status": {
                       "passed": true
                     },
                     "temperature": {
                       "current": 30
                     }
                   }
                   """;

        var status = SmartCtlParser.Parse(json);

        Assert.NotNull(status);
        Assert.Equal("sda", status!.DeviceIdentifier);
        Assert.True(status.IsHealthy);
        Assert.Equal(30, status.TemperatureCelsius);
        Assert.Equal(123, status.PowerOnHours);
        Assert.Equal(7, status.PercentageUsed);
        Assert.Equal(42, status.PowerCycles);
    }
}
