using DumbNetwork.Services;
using DumbNetwork.Services.Firewall;
using DumbNetwork.Strategies;

namespace DumbNetwork.Tests;

public class ZapretRunnerTests
{
    [Fact]
    public async Task Start_without_privileges_fails_before_touching_firewall()
    {
        if (OperatingSystem.IsWindows() || PrivilegeService.IsElevated())
        {
            return;
        }

        var firewall = new RecordingFirewall();
        await using var runner = new ZapretRunner(firewall);

        var exception = await Assert.ThrowsAsync<PrivilegesRequiredException>(
            () => runner.StartAsync(StrategyCatalog.All[0]));

        Assert.Contains("root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RunnerState.Failed, runner.State);
        Assert.False(firewall.Applied);
        Assert.Null(runner.ActiveStrategy);
    }

    [Fact]
    public async Task Stop_when_already_stopped_reports_terminal_state()
    {
        var runner = new ZapretRunner(new RecordingFirewall());
        var statuses = new List<RunnerStatus>();
        runner.StatusChanged += statuses.Add;

        await runner.StopAsync();

        var status = Assert.Single(statuses);
        Assert.Equal(RunnerState.Stopped, status.State);

        await runner.DisposeAsync();
    }

    private sealed class RecordingFirewall : IFirewallManager
    {
        public string Name => "test";

        public bool Applied { get; private set; }

        public Task ApplyAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
        {
            Applied = true;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(CancellationToken cancellationToken = default)
        {
            Applied = false;
            return Task.CompletedTask;
        }
    }
}
