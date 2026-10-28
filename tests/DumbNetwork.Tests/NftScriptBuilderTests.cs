using DumbNetwork.Services;
using DumbNetwork.Services.Firewall;
using DumbNetwork.Strategies;

namespace DumbNetwork.Tests;

public class NftScriptBuilderTests
{
    private static readonly StrategyDefinition Strategy =
        StrategyCatalog.All.Single(strategy => strategy.Id == "fake-tls-quic");

    [Fact]
    public void Script_creates_dedicated_table_and_postrouting_chain()
    {
        var script = NftScriptBuilder.Build(Strategy);

        Assert.Contains($"add table inet {ZapretConstants.NftTable}", script);
        Assert.Contains("type filter hook postrouting priority mangle", script);
        Assert.Contains($"queue num {ZapretConstants.QueueNumber} bypass", script);
        Assert.Contains($"meta mark and {ZapretConstants.FwMark} == 0", script);
    }

    [Fact]
    public void Script_queues_only_the_strategy_ports()
    {
        var script = NftScriptBuilder.Build(Strategy);

        Assert.Contains("tcp dport { 80, 443 }", script);
        Assert.Contains("udp dport { 443 }", script);
        Assert.Contains("ct original packets 1-6", script);
        Assert.Contains("oifname != \"lo\"", script);
    }

    [Fact]
    public void Script_can_omit_conntrack_limits()
    {
        var script = NftScriptBuilder.Build(Strategy, useConnBytes: false);

        Assert.DoesNotContain("ct original packets", script);
        Assert.Contains("queue num 200 bypass", script);
    }

    [Fact]
    public void Strategy_without_tcp_does_not_emit_tcp_rule()
    {
        var strategy = StrategyCatalog.All.Single(item => item.Id == "quic-only");

        var script = NftScriptBuilder.Build(strategy);

        Assert.DoesNotContain("tcp dport", script);
        Assert.Contains("udp dport { 443 }", script);
    }
}
