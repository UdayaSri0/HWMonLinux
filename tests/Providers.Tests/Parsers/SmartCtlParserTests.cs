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
    }
}
