using HwMonLinux.Providers.Parsers;
using Xunit;

namespace HwMonLinux.Providers.Tests.Parsers;

public class MemInfoParserTests
{
    [Fact]
    public void Parse_ShouldCalculateUsage()
    {
        var text = """
                   MemTotal:       8000000 kB
                   MemAvailable:   2000000 kB
                   Buffers:        0 kB
                   """;

        var snapshot = MemInfoParser.Parse(text);

        Assert.Equal(8000000L * 1024, snapshot.TotalBytes);
        Assert.Equal(2000000L * 1024, snapshot.AvailableBytes);
        Assert.InRange(snapshot.UsagePercentage, 70, 80);
    }
}
