using DumbNetwork.Services;

namespace DumbNetwork.Tests;

public class SudoDiagnosticsTests
{
    [Fact]
    public void Wrong_password_is_retryable()
    {
        var failure = SudoDiagnostics.TryMap(
            "__DUMBNET_SUDO_PASSWORD__Sorry, try again.\n");

        Assert.NotNull(failure);
        Assert.True(failure!.CanRetry);
        Assert.Contains("пароль", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_sudoers_entry_is_not_retryable()
    {
        var failure = SudoDiagnostics.TryMap(
            "user is not in the sudoers file. This incident will be reported.");

        Assert.NotNull(failure);
        Assert.False(failure!.CanRetry);
        Assert.Contains("sudoers", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_tty_is_reported()
    {
        var failure = SudoDiagnostics.TryMap(
            "sudo: a terminal is required to read the password; either use the -S option to read from standard input or configure an askpass helper");

        Assert.NotNull(failure);
        Assert.False(failure!.CanRetry);
        Assert.Contains("терминал", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_stderr_falls_back_to_exit_code()
    {
        var failure = SudoDiagnostics.BuildAuthFailure(1, "netlink: something odd");

        Assert.False(failure.CanRetry);
        Assert.Contains("код 1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("netlink", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_marker_is_not_leaked_into_message()
    {
        var failure = SudoDiagnostics.BuildAuthFailure(
            1,
            "__DUMBNET_SUDO_PASSWORD__sudo: a password is required\n");

        Assert.DoesNotContain("DUMBNET", failure.Message, StringComparison.Ordinal);
    }
}
