using DumbNetwork.Strategies;

namespace DumbNetwork.Services.Firewall;

public sealed class FirewallException : Exception
{
    public FirewallException(string message) : base(message)
    {
    }
}

public interface IFirewallManager
{
    string Name { get; }

    Task ApplyAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default);

    Task RemoveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Windows: winws filters packets through WinDivert itself.</summary>
public sealed class NoopFirewall : IFirewallManager
{
    public string Name => "WinDivert";

    public Task ApplyAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public static class FirewallFactory
{
    public static IFirewallManager Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new NoopFirewall();
        }

        if (OperatingSystem.IsLinux())
        {
            if (CommandRunner.ExistsOnPath("nft"))
            {
                return new NftFirewall();
            }

            if (CommandRunner.ExistsOnPath("iptables"))
            {
                return new IptablesFirewall();
            }
        }

        return new UnsupportedFirewall();
    }
}

public sealed class UnsupportedFirewall : IFirewallManager
{
    public string Name => "нет";

    public Task ApplyAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
        => throw new FirewallException(
            "Не найден nft или iptables. Установите nftables (рекомендуется) либо iptables.");

    public Task RemoveAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
