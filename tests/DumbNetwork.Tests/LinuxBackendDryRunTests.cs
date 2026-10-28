using DumbNetwork.Services;
using DumbNetwork.Strategies;

namespace DumbNetwork.Tests;

/// <summary>
/// Runs the real nfqws binary with --dry-run. This validates that every Linux
/// strategy is syntactically accepted by the bundled backend version.
/// </summary>
public class LinuxBackendDryRunTests
{
    [Theory]
    [MemberData(nameof(LinuxStrategies))]
    public async Task Nfqws_accepts_strategy_arguments(StrategyDefinition strategy)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var layout = BackendLayout.Locate();
        var arguments = ArgumentBuilder.Build(strategy, layout).Prepend("--dry-run");

        var result = await CommandRunner.RunAsync(layout.ExecutablePath, arguments);

        Assert.True(result.Success, $"{strategy.Id}: {result.Describe()}");
    }

    public static TheoryData<StrategyDefinition> LinuxStrategies()
    {
        var data = new TheoryData<StrategyDefinition>();
        foreach (var strategy in StrategyCatalog.ForPlatform(isWindows: false))
        {
            data.Add(strategy);
        }

        return data;
    }
}
