using System.Text.RegularExpressions;
using DumbNetwork.Services;
using DumbNetwork.Strategies;

namespace DumbNetwork.Tests;

public class StrategyCatalogTests
{
    [Fact]
    public void Strategy_ids_are_unique()
    {
        var ids = StrategyCatalog.All.Select(strategy => strategy.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(AllStrategies))]
    public void Strategy_has_arguments_and_ports(StrategyDefinition strategy)
    {
        Assert.NotEmpty(strategy.Arguments);
        Assert.False(string.IsNullOrWhiteSpace(strategy.Name));
        Assert.False(string.IsNullOrWhiteSpace(strategy.Description));
        Assert.True(strategy.TcpPorts.Count > 0 || strategy.UdpPorts.Count > 0);
    }

    [Theory]
    [MemberData(nameof(AllStrategies))]
    public void Every_fake_placeholder_points_to_a_bundled_file(StrategyDefinition strategy)
    {
        var layout = BackendLayout.Locate();
        var tokenRegex = new Regex(@"\{fake:([^}]+)\}", RegexOptions.CultureInvariant);

        foreach (var argument in strategy.Arguments)
        {
            foreach (Match match in tokenRegex.Matches(argument))
            {
                var path = Path.Combine(layout.FakeDirectory, match.Groups[1].Value);
                Assert.True(File.Exists(path), $"Missing fake payload: {path}");
            }
        }
    }

    [Fact]
    public void Linux_catalog_contains_classic_strategy()
    {
        var strategies = StrategyCatalog.ForPlatform(isWindows: false);

        Assert.Contains(strategies, strategy => strategy.Id == "classic-bolvan");
    }

    [Fact]
    public void Windows_catalog_hides_linux_only_strategy()
    {
        var strategies = StrategyCatalog.ForPlatform(isWindows: true);

        Assert.DoesNotContain(strategies, strategy => strategy.Id == "classic-bolvan");
        Assert.All(strategies, strategy => Assert.NotEqual(PlatformSupport.Linux, strategy.Platforms));
    }

    public static TheoryData<StrategyDefinition> AllStrategies()
    {
        var data = new TheoryData<StrategyDefinition>();
        foreach (var strategy in StrategyCatalog.All)
        {
            data.Add(strategy);
        }

        return data;
    }
}
