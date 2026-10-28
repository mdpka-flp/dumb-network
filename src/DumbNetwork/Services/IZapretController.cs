using DumbNetwork.Strategies;

namespace DumbNetwork.Services;

/// <summary>
/// Abstraction over the code that controls nfqws/winws and the firewall.
/// On Linux the unprivileged GUI talks to a separate root helper; on Windows
/// (and when the Linux GUI is already root) the local runner is used directly.
/// </summary>
public interface IZapretController : IAsyncDisposable
{
    event Action<string>? LogReceived;

    event Action<RunnerStatus>? StatusChanged;

    RunnerState State { get; }

    StrategyDefinition? ActiveStrategy { get; }

    /// <summary>
    /// True when the controller cannot start until the user authenticates.
    /// Local runners are already privileged, so they return false.
    /// </summary>
    bool NeedsAuthentication { get; }

    string FirewallName { get; }

    /// <summary>Obtains the privileges required by the controller for this session.</summary>
    Task AuthenticateAsync(string password, CancellationToken cancellationToken = default);

    Task StartAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
