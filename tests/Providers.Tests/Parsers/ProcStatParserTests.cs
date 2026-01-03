using HwMonLinux.Providers.Parsers;
using Xunit;

namespace HwMonLinux.Providers.Tests.Parsers;

public class ProcStatParserTests
{
    [Fact]
    public void Parse_ShouldReturnCpuEntries()
    {
        var text = """
                   cpu  122 34 56 789 10 11 12 13
                   cpu0 100 20 30 400 5 5 5 5
                   cpu1 22 14 26 389 5 6 7 8
                   intr 1
                   """;

        var snapshot = ProcStatParser.Parse(text);

        Assert.Equal(3, snapshot.Count);
        Assert.True(snapshot.ContainsKey("cpu"));
        Assert.True(snapshot.ContainsKey("cpu0"));
        Assert.True(snapshot.ContainsKey("cpu1"));
    }

    [Fact]
    public void CalculateUsage_ComputesPercentage()
    {
        var first = new CpuTimes(100, 0, 0, 900, 0, 0, 0, 0);
        var second = new CpuTimes(150, 0, 0, 950, 0, 0, 0, 0);

        var usage = second.CalculateUsage(first);

        Assert.InRange(usage, 40, 60);
    }
}
