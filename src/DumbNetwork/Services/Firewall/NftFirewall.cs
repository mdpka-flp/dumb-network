using DumbNetwork.Strategies;

namespace DumbNetwork.Services.Firewall;

public sealed class NftFirewall : IFirewallManager
{
    public string Name => "nftables";

    public async Task ApplyAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
    {
        await RemoveAsync(cancellationToken);

        var result = await RunScriptAsync(
            NftScriptBuilder.Build(strategy, useConnBytes: true),
            cancellationToken);

        if (!result.Success && LooksLikeConntrackError(result))
        {
            await RemoveAsync(cancellationToken);
            result = await RunScriptAsync(
                NftScriptBuilder.Build(strategy, useConnBytes: false),
                cancellationToken);
        }

        if (!result.Success)
        {
            throw new FirewallException($"nft не смог применить правила: {result.Describe()}");
        }
    }

    public Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync(
            "nft",
            ["delete", "table", "inet", ZapretConstants.NftTable],
            cancellationToken: cancellationToken);
    }

    private static Task<CommandResult> RunScriptAsync(string script, CancellationToken cancellationToken)
    {
        return CommandRunner.RunAsync("nft", ["-f", "-"], script, cancellationToken);
    }

    private static bool LooksLikeConntrackError(CommandResult result)
    {
        var text = result.Describe();
        return text.Contains("ct original", StringComparison.OrdinalIgnoreCase)
            || text.Contains("conntrack", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unknown expression", StringComparison.OrdinalIgnoreCase)
            || text.Contains("packets", StringComparison.OrdinalIgnoreCase);
    }
}
