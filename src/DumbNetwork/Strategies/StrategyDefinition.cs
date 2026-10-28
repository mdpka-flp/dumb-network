namespace DumbNetwork.Strategies;

/// <summary>Where a strategy's nfqws/winws arguments are known to work.</summary>
public enum PlatformSupport
{
    All,
    Linux,
    Windows,
}

/// <summary>
/// A ready-made zapret strategy. <see cref="Arguments"/> are passed verbatim to
/// nfqws (Linux) or winws (Windows). The token <c>{fake:file.bin}</c> is replaced
/// with an absolute path inside the bundled <c>files/fake</c> directory.
/// </summary>
public sealed record StrategyDefinition(
    string Id,
    string Name,
    string Description,
    string Badge,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> TcpPorts,
    IReadOnlyList<string> UdpPorts,
    PlatformSupport Platforms = PlatformSupport.All)
{
    public string PlatformLabel => Platforms switch
    {
        PlatformSupport.Linux => "Linux",
        PlatformSupport.Windows => "Windows",
        _ => "Linux + Windows",
    };

    public string PortSummary
    {
        get
        {
            var parts = new List<string>(2);
            if (TcpPorts.Count > 0)
            {
                parts.Add($"TCP {string.Join(", ", TcpPorts)}");
            }

            if (UdpPorts.Count > 0)
            {
                parts.Add($"UDP {string.Join(", ", UdpPorts)}");
            }

            return parts.Count == 0 ? "все порты" : string.Join(" · ", parts);
        }
    }
}
