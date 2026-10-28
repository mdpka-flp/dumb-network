using DumbNetwork.Services;
using DumbNetwork.Services.Firewall;
using DumbNetwork.Strategies;

namespace DumbNetwork.Tests;

/// <summary>
/// Opt-in end-to-end test for the Linux orchestration. It installs real nftables
/// rules and starts a stand-in backend process, so run it in an isolated network
/// namespace:
/// <code>
/// unshare -Urn sh -c 'ip link set lo up; DUMBZAPRET_INTEGRATION=1 \
///   dotnet test --filter LinuxIntegrationTests'
/// </code>
/// A plain root shell also works.
/// </summary>
public class LinuxIntegrationTests
{
    [Fact]
    public async Task Start_and_stop_apply_and_remove_nft_rules()
    {
        if (Environment.GetEnvironmentVariable("DUMBZAPRET_INTEGRATION") != "1")
        {
            return;
        }

        if (!OperatingSystem.IsLinux() || !PrivilegeService.IsElevated())
        {
            return;
        }

        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"dumb-network-it-{Guid.NewGuid():N}");
        var platformDirectory = Path.Combine(temporaryRoot, "linux-x64");
        var fakeDirectory = Path.Combine(temporaryRoot, "files", "fake");
        var executable = Path.Combine(platformDirectory, "nfqws");
        Directory.CreateDirectory(platformDirectory);
        Directory.CreateDirectory(fakeDirectory);

        // Stand-in for nfqws: keeps running until the runner kills it.
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nexec sleep 60\n");
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var layout = new BackendLayout(
            temporaryRoot,
            "integration",
            platformDirectory,
            executable,
            fakeDirectory,
            "linux-x64",
            IsWindows: false);

        var firewall = FirewallFactory.Create();
        Assert.IsType<NftFirewall>(firewall);

        try
        {
            await using var runner = new ZapretRunner(firewall, layout);
            var strategy = StrategyCatalog.All.Single(item => item.Id == "quic-only");

            await runner.StartAsync(strategy);

            Assert.Equal(RunnerState.Running, runner.State);
            Assert.NotNull(runner.ActiveStrategy);

            var applied = await CommandRunner.RunAsync(
                "nft",
                ["list", "table", "inet", ZapretConstants.NftTable]);
            Assert.True(applied.Success, applied.Describe());
            Assert.Contains("queue", applied.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                ZapretConstants.QueueNumber.ToString(),
                applied.StandardOutput,
                StringComparison.Ordinal);

            await runner.StopAsync();

            Assert.Equal(RunnerState.Stopped, runner.State);
            Assert.Null(runner.ActiveStrategy);

            var removed = await CommandRunner.RunAsync(
                "nft",
                ["list", "table", "inet", ZapretConstants.NftTable]);
            Assert.False(removed.Success, removed.StandardOutput);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
