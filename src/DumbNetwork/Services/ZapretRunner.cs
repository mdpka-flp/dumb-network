using System.Diagnostics;
using DumbNetwork.Strategies;
using DumbNetwork.Services.Firewall;

namespace DumbNetwork.Services;

public enum RunnerState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}

public sealed record RunnerStatus(RunnerState State, string? Message);

public sealed class ZapretException : Exception
{
    public ZapretException(string message) : base(message)
    {
    }
}

public sealed class PrivilegesRequiredException : Exception
{
    public PrivilegesRequiredException(string message) : base(message)
    {
    }
}

/// <summary>
/// Owns the nfqws/winws child process and the platform firewall state.
/// Only one strategy can be active at a time.
/// </summary>
public sealed class ZapretRunner : IZapretController
{
    private readonly IFirewallManager _firewall;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BackendLayout? _fixedBackend;
    private Process? _process;
    private BackendLayout? _layout;
    private bool _disposed;

    public ZapretRunner(IFirewallManager firewall, BackendLayout? backend = null)
    {
        _firewall = firewall;
        _fixedBackend = backend;
    }

    public static ZapretRunner CreateDefault() => new(FirewallFactory.Create());

    public event Action<string>? LogReceived;

    public event Action<RunnerStatus>? StatusChanged;

    public RunnerState State { get; private set; } = RunnerState.Stopped;

    public StrategyDefinition? ActiveStrategy { get; private set; }

    public BackendLayout? Backend => _layout;

    public string FirewallName => _firewall.Name;

    public bool NeedsAuthentication => false;

    public Task AuthenticateAsync(string password, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task StartAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (State is RunnerState.Running or RunnerState.Starting)
            {
                return;
            }

            SetStatus(RunnerState.Starting, "Запуск zapret…");

            _layout ??= _fixedBackend ?? BackendLayout.Locate();

            if (!PrivilegeService.IsElevated())
            {
                throw new PrivilegesRequiredException(PrivilegeService.RequirementHint);
            }

            var arguments = ArgumentBuilder.Build(strategy, _layout);
            Log($"Стратегия «{strategy.Name}» ({strategy.PlatformLabel})");
            Log($"Бэкенд: {_layout.DisplayName}");
            Log($"Команда: {CommandRunner.FormatCommandLine(_layout.ExecutablePath, arguments)}");

            _process = CreateProcess(_layout, arguments);
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            ActiveStrategy = strategy;

            // Give the backend a moment to validate its arguments and bind the queue.
            await Task.Delay(300, cancellationToken);
            if (_process.HasExited)
            {
                var exitCode = _process.ExitCode;
                DisposeProcess();
                ActiveStrategy = null;
                throw new ZapretException(
                    $"Бэкенд завершился с кодом {exitCode}. Проверьте журнал и выбранную стратегию.");
            }

            SetStatus(RunnerState.Starting, "Настройка firewall…");
            await _firewall.ApplyAsync(strategy, cancellationToken);

            SetStatus(RunnerState.Running, $"{_firewall.Name} · {strategy.Name}");
        }
        catch (Exception ex)
        {
            Log($"Ошибка запуска: {ex.Message}");
            await SafeRemoveFirewallAsync();
            DisposeProcess();
            ActiveStrategy = null;
            SetStatus(RunnerState.Failed, ex.Message);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (State == RunnerState.Stopped && _process is null)
            {
                // Still acknowledge the request: the helper client waits for a
                // terminal state after sending "stop". This also covers a
                // failed start that never reached the runner.
                SetStatus(RunnerState.Stopped, "Выключено");
                return;
            }

            SetStatus(RunnerState.Stopping, "Остановка…");

            await SafeRemoveFirewallAsync();
            DisposeProcess();
            ActiveStrategy = null;

            SetStatus(RunnerState.Stopped, "Выключено");
        }
        finally
        {
            _gate.Release();
        }
    }

    private Process CreateProcess(BackendLayout layout, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = layout.ExecutablePath,
            WorkingDirectory = layout.PlatformDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                Log(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                Log($"[stderr] {eventArgs.Data}");
            }
        };

        process.Exited += OnProcessExited;
        return process;
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        if (State != RunnerState.Running)
        {
            return;
        }

        _ = HandleUnexpectedExitAsync();
    }

    private async Task HandleUnexpectedExitAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State != RunnerState.Running)
            {
                return;
            }

            Log("Процесс zapret завершился неожиданно.");
            await SafeRemoveFirewallAsync();
            DisposeProcess();
            ActiveStrategy = null;
            SetStatus(RunnerState.Failed, "Процесс завершился. Подробности в журнале.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SafeRemoveFirewallAsync()
    {
        try
        {
            await _firewall.RemoveAsync();
        }
        catch (Exception ex)
        {
            Log($"Не удалось снять правила firewall: {ex.Message}");
        }
    }

    private void DisposeProcess()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log($"Не удалось остановить процесс: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private void SetStatus(RunnerState state, string? message)
    {
        State = state;
        StatusChanged?.Invoke(new RunnerStatus(state, message));
    }

    private void Log(string message) => LogReceived?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        _gate.Dispose();
    }
}
