using DumbNetwork.Strategies;

namespace DumbNetwork.Services.Firewall;

/// <summary>
/// Fallback for systems without nftables. Uses a dedicated mangle chain so the
/// rules can be removed without touching unrelated firewall state.
/// </summary>
public sealed class IptablesFirewall : IFirewallManager
{
    private readonly string[] _binaries;

    public IptablesFirewall()
    {
        _binaries = new[] { "iptables", "ip6tables" }
            .Where(CommandRunner.ExistsOnPath)
            .ToArray();

        if (_binaries.Length == 0)
        {
            throw new FirewallException("Не найден iptables/ip6tables.");
        }
    }

    public string Name => "iptables";

    public async Task ApplyAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
    {
        foreach (var binary in _binaries)
        {
            await RunAsync(binary, ["-t", "mangle", "-N", ZapretConstants.IptablesChain], cancellationToken);
            await RunAsync(binary, ["-t", "mangle", "-F", ZapretConstants.IptablesChain], cancellationToken);

            var jump = await RunAsync(
                binary,
                ["-t", "mangle", "-C", "POSTROUTING", "-j", ZapretConstants.IptablesChain],
                cancellationToken);

            if (!jump.Success)
            {
                var insert = await RunAsync(
                    binary,
                    ["-t", "mangle", "-I", "POSTROUTING", "1", "-j", ZapretConstants.IptablesChain],
                    cancellationToken);

                if (!insert.Success)
                {
                    throw new FirewallException($"{binary}: {insert.Describe()}");
                }
            }

            await AppendRulesAsync(binary, strategy, useConnBytes: true, cancellationToken);
        }
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        foreach (var binary in _binaries)
        {
            await RunAsync(
                binary,
                ["-t", "mangle", "-D", "POSTROUTING", "-j", ZapretConstants.IptablesChain],
                cancellationToken);
            await RunAsync(binary, ["-t", "mangle", "-F", ZapretConstants.IptablesChain], cancellationToken);
            await RunAsync(binary, ["-t", "mangle", "-X", ZapretConstants.IptablesChain], cancellationToken);
        }
    }

    private async Task AppendRulesAsync(
        string binary,
        StrategyDefinition strategy,
        bool useConnBytes,
        CancellationToken cancellationToken)
    {
        var results = new List<CommandResult>();

        if (strategy.TcpPorts.Count > 0)
        {
            results.Add(await AppendRuleAsync(binary, "tcp", strategy.TcpPorts, useConnBytes, cancellationToken));
        }

        if (strategy.UdpPorts.Count > 0)
        {
            results.Add(await AppendRuleAsync(binary, "udp", strategy.UdpPorts, useConnBytes, cancellationToken));
        }

        var failed = results.FirstOrDefault(result => !result.Success);
        if (failed is null)
        {
            return;
        }

        if (useConnBytes && LooksLikeConntrackError(failed))
        {
            await RunAsync(binary, ["-t", "mangle", "-F", ZapretConstants.IptablesChain], cancellationToken);
            await AppendRulesAsync(binary, strategy, useConnBytes: false, cancellationToken);
            return;
        }

        throw new FirewallException($"{binary}: {failed.Describe()}");
    }

    private static Task<CommandResult> AppendRuleAsync(
        string binary,
        string protocol,
        IReadOnlyList<string> ports,
        bool useConnBytes,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-t", "mangle", "-A", ZapretConstants.IptablesChain,
            "!", "-o", "lo",
            "-m", "mark", "!", "--mark", $"{ZapretConstants.FwMark}/{ZapretConstants.FwMark}",
            "-p", protocol,
            "-m", "multiport", "--dports", ToIptablesPorts(ports),
        };

        if (useConnBytes)
        {
            arguments.AddRange(
                ["-m", "connbytes", "--connbytes-dir=original", "--connbytes-mode=packets", "--connbytes", "1:6"]);
        }

        arguments.AddRange(
            ["-j", "NFQUEUE", "--queue-num", ZapretConstants.QueueNumber.ToString(), "--queue-bypass"]);

        return CommandRunner.RunAsync(binary, arguments, cancellationToken: cancellationToken);
    }

    private static string ToIptablesPorts(IReadOnlyList<string> ports)
    {
        // nft uses "50000-50100", iptables multiport expects "50000:50100".
        return string.Join(",", ports.Select(port => port.Replace('-', ':')));
    }

    private static Task<CommandResult> RunAsync(
        string binary,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        return CommandRunner.RunAsync(binary, ["-w", .. arguments], cancellationToken: cancellationToken);
    }

    private static bool LooksLikeConntrackError(CommandResult result)
    {
        var text = result.Describe();
        return text.Contains("connbytes", StringComparison.OrdinalIgnoreCase)
            || text.Contains("conntrack", StringComparison.OrdinalIgnoreCase)
            || text.Contains("No chain/target/match", StringComparison.OrdinalIgnoreCase);
    }
}
