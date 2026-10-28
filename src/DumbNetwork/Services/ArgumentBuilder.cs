using System.Text.RegularExpressions;
using DumbNetwork.Strategies;

namespace DumbNetwork.Services;

/// <summary>Builds the platform-specific command line for a strategy.</summary>
public static partial class ArgumentBuilder
{
    public static IReadOnlyList<string> Build(StrategyDefinition strategy, BackendLayout layout)
    {
        var arguments = new List<string>(strategy.Arguments.Count + 4);

        if (layout.IsWindows)
        {
            // winws performs the WinDivert filtering itself; no external firewall.
            if (strategy.TcpPorts.Count > 0)
            {
                arguments.Add($"--wf-tcp={string.Join(',', strategy.TcpPorts)}");
            }

            if (strategy.UdpPorts.Count > 0)
            {
                arguments.Add($"--wf-udp={string.Join(',', strategy.UdpPorts)}");
            }
        }
        else
        {
            arguments.Add($"--qnum={ZapretConstants.QueueNumber}");
            arguments.Add($"--dpi-desync-fwmark={ZapretConstants.FwMark}");
        }

        foreach (var argument in strategy.Arguments)
        {
            arguments.Add(ExpandFake(argument, layout.FakeDirectory));
        }

        return arguments;
    }

    public static string ExpandFake(string argument, string fakeDirectory)
    {
        return FakeTokenRegex().Replace(
            argument,
            match => Path.Combine(fakeDirectory, match.Groups[1].Value));
    }

    [GeneratedRegex(@"\{fake:([^}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex FakeTokenRegex();
}
