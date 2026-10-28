using System.Text;
using DumbNetwork.Strategies;

namespace DumbNetwork.Services.Firewall;

/// <summary>
/// Generates a self-contained nftables table. Packets are queued in the
/// postrouting hook (priority mangle), matching the official zapret recipe.
/// </summary>
public static class NftScriptBuilder
{
    public static string Build(StrategyDefinition strategy, bool useConnBytes = true)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"add table inet {ZapretConstants.NftTable}");
        builder.AppendLine(
            $"add chain inet {ZapretConstants.NftTable} post {{ type filter hook postrouting priority mangle; }}");

        AppendRule(builder, "tcp", strategy.TcpPorts, useConnBytes);
        AppendRule(builder, "udp", strategy.UdpPorts, useConnBytes);

        return builder.ToString();
    }

    private static void AppendRule(
        StringBuilder builder,
        string protocol,
        IReadOnlyList<string> ports,
        bool useConnBytes)
    {
        if (ports.Count == 0)
        {
            return;
        }

        var conntrack = useConnBytes ? "ct original packets 1-6 " : string.Empty;
        builder.AppendLine(
            $"add rule inet {ZapretConstants.NftTable} post "
            + "oifname != \"lo\" "
            + $"meta mark and {ZapretConstants.FwMark} == 0 "
            + $"{protocol} dport {{ {string.Join(", ", ports)} }} "
            + conntrack
            + $"queue num {ZapretConstants.QueueNumber} bypass");
    }
}
