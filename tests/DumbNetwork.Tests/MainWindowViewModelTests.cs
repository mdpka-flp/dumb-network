using DumbNetwork.Services;
using DumbNetwork.Strategies;
using DumbNetwork.ViewModels;

namespace DumbNetwork.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public async Task Wrong_password_reopens_prompt_with_error_then_starts()
    {
        var controller = new FakeController();
        var viewModel = new MainWindowViewModel(controller);
        var promptedErrors = new List<string?>();

        viewModel.PasswordPrompt = error =>
        {
            promptedErrors.Add(error);
            return Task.FromResult<string?>(error is null ? "wrong" : "correct");
        };

        await viewModel.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(new string?[] { null, "Неверный пароль администратора." }, promptedErrors);
        Assert.Equal(2, controller.AuthenticationAttempts);
        Assert.Equal(1, controller.StartCount);
    }

    [Fact]
    public async Task Cancelled_password_does_not_start()
    {
        var controller = new FakeController();
        var viewModel = new MainWindowViewModel(controller)
        {
            PasswordPrompt = _ => Task.FromResult<string?>(null),
        };

        await viewModel.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(0, controller.AuthenticationAttempts);
        Assert.Equal(0, controller.StartCount);
        Assert.Contains("отменено", viewModel.StatusDetails, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_retryable_authentication_error_does_not_loop()
    {
        var controller = new FakeController
        {
            AuthenticationFailure = new AuthenticationFailedException("sudoers", canRetry: false),
        };
        var viewModel = new MainWindowViewModel(controller);
        var prompts = 0;

        viewModel.PasswordPrompt = _ =>
        {
            prompts++;
            return Task.FromResult<string?>("password");
        };

        await viewModel.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(1, prompts);
        Assert.Equal(1, controller.AuthenticationAttempts);
        Assert.Equal(0, controller.StartCount);
        Assert.Contains("sudoers", viewModel.StatusDetails, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeController : IZapretController
    {
        public event Action<string>? LogReceived;
        public event Action<RunnerStatus>? StatusChanged;

        public RunnerState State { get; private set; } = RunnerState.Stopped;

        public StrategyDefinition? ActiveStrategy { get; private set; }

        public bool NeedsAuthentication { get; private set; } = true;

        public string FirewallName => "test";

        public int AuthenticationAttempts { get; private set; }

        public int StartCount { get; private set; }

        public AuthenticationFailedException? AuthenticationFailure { get; init; }

        public Task AuthenticateAsync(string password, CancellationToken cancellationToken = default)
        {
            AuthenticationAttempts++;

            if (AuthenticationFailure is not null)
            {
                throw AuthenticationFailure;
            }

            if (AuthenticationAttempts == 1)
            {
                throw new AuthenticationFailedException("Неверный пароль администратора.", canRetry: true);
            }

            NeedsAuthentication = false;
            return Task.CompletedTask;
        }

        public Task StartAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
        {
            StartCount++;
            ActiveStrategy = strategy;
            State = RunnerState.Running;
            LogReceived?.Invoke("started");
            StatusChanged?.Invoke(new RunnerStatus(RunnerState.Running, "running"));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = RunnerState.Stopped;
            ActiveStrategy = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
