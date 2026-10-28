using DumbNetwork.Services;
using DumbNetwork.Strategies;

namespace DumbNetwork.Tests;

public class ArgumentBuilderTests
{
    private static readonly StrategyDefinition Strategy = StrategyCatalog.All[0];

    private static BackendLayout Layout(bool isWindows)
    {
        var root = Path.Combine(Path.GetTempPath(), "dumb-network-tests");
        var platform = isWindows ? "windows-x64" : "linux-x64";
        var executable = isWindows ? "winws.exe" : "nfqws";
        return new BackendLayout(
            root,
            "v72.13",
            Path.Combine(root, platform),
            Path.Combine(root, platform, executable),
            Path.Combine(root, "files", "fake"),
            platform,
            isWindows);
    }

    [Fact]
    public void Linux_arguments_use_nfqueue_and_expand_fake_paths()
    {
        var layout = Layout(isWindows: false);

        var arguments = ArgumentBuilder.Build(Strategy, layout);

        Assert.Contains($"--qnum={ZapretConstants.QueueNumber}", arguments);
        Assert.Contains($"--dpi-desync-fwmark={ZapretConstants.FwMark}", arguments);
        Assert.Contains(arguments, argument => argument.StartsWith("--dpi-desync-fake-tls=", StringComparison.Ordinal)
            && argument.Contains(layout.FakeDirectory, StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains("{fake:", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_arguments_use_windivert_filters_and_no_queue_options()
    {
        var layout = Layout(isWindows: true);

        var arguments = ArgumentBuilder.Build(Strategy, layout);

        Assert.Contains("--wf-tcp=80,443", arguments);
        Assert.Contains("--wf-udp=443", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--qnum=", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--dpi-desync-fwmark=", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains("{fake:", StringComparison.Ordinal));
    }

    [Fact]
    public void Strategy_without_tcp_ports_has_no_tcp_windivert_filter()
    {
        var strategy = StrategyCatalog.All.Single(item => item.Id == "quic-only");

        var arguments = ArgumentBuilder.Build(strategy, Layout(isWindows: true));

        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--wf-tcp=", StringComparison.Ordinal));
        Assert.Contains("--wf-udp=443", arguments);
    }
}
