using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DumbNetwork.Strategies;

namespace DumbNetwork.Services;

public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message, bool canRetry) : base(message)
    {
        CanRetry = canRetry;
    }

    /// <summary>True for a wrong password, false for configuration/timeout errors.</summary>
    public bool CanRetry { get; }
}

/// <summary>
/// Unprivileged side of the Linux privilege separation. It launches the same
/// executable with <see cref="PrivilegedHelper.CommandArgument"/> through
/// <c>sudo -S</c>, answers sudo's password prompt from stdin, and then speaks
/// JSON lines with the root helper.
/// </summary>
public sealed class PrivilegedHelperClient : IZapretController
{
    private static readonly TimeSpan AuthenticationTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(25);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();

    private Process? _process;
    private StreamWriter? _input;
    private bool _authenticated;
    private bool _disposed;
    private RunnerState _state = RunnerState.Stopped;
    private StrategyDefinition? _activeStrategy;
    private StrategyDefinition? _requestedStrategy;
    private TaskCompletionSource<RunnerStatus>? _pendingState;
    private string _stderrTail = string.Empty;

    public event Action<string>? LogReceived;

    public event Action<RunnerStatus>? StatusChanged;

    public RunnerState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public StrategyDefinition? ActiveStrategy
    {
        get
        {
            lock (_sync)
            {
                return _activeStrategy;
            }
        }
    }

    public bool NeedsAuthentication
    {
        get
        {
            lock (_sync)
            {
                return !IsHelperAliveLocked();
            }
        }
    }

    public string FirewallName => "nftables / iptables (helper)";

    public async Task AuthenticateAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new AuthenticationFailedException("Введите пароль администратора.", canRetry: false);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PrivilegedHelperClient));
            }

            lock (_sync)
            {
                if (IsHelperAliveLocked())
                {
                    return;
                }
            }

            KillHelper();

            var (executable, arguments) = BuildHelperCommand();
            var startInfo = BuildSudoStartInfo(executable, arguments);

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new AuthenticationFailedException("Не удалось запустить sudo.", canRetry: false);
            }
            catch (Win32Exception ex)
            {
                throw new AuthenticationFailedException(
                    $"Не удалось запустить sudo: {ex.Message}. Убедитесь, что sudo установлен.",
                    canRetry: false);
            }

            var auth = new TaskCompletionSource<AuthFailure?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var prompt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sync)
            {
                _process = process;
                _input = process.StandardInput;
                _authenticated = false;
                _state = RunnerState.Stopped;
                _activeStrategy = null;
                _requestedStrategy = null;
                _stderrTail = string.Empty;
            }

            _ = MonitorStdoutAsync(process, auth);
            _ = MonitorStderrAsync(process, auth, prompt);
            _ = MonitorExitAsync(process, auth);
            _ = AnswerPasswordAsync(prompt.Task, auth.Task, process.StandardInput, password);

            AuthFailure? failure;
            try
            {
                failure = await auth.Task.WaitAsync(AuthenticationTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                failure = new AuthFailure(
                    "Не удалось получить права root: helper не ответил вовремя.",
                    CanRetry: false);
            }
            catch (OperationCanceledException)
            {
                KillHelper();
                throw;
            }

            if (failure is not null)
            {
                KillHelper();
                throw new AuthenticationFailedException(failure.Message, failure.CanRetry);
            }

            var alive = false;
            lock (_sync)
            {
                alive = ReferenceEquals(_process, process) && IsHelperAliveLocked();
            }

            if (!alive)
            {
                KillHelper();
                throw new AuthenticationFailedException(
                    "Helper завершился сразу после подтверждения прав.",
                    canRetry: false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartAsync(StrategyDefinition strategy, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PrivilegedHelperClient));
            }

            lock (_sync)
            {
                if (!IsHelperAliveLocked())
                {
                    throw new PrivilegesRequiredException(
                        "Для запуска нужно подтвердить пароль администратора.");
                }

                if (_state == RunnerState.Running)
                {
                    return;
                }
            }

            var pending = new TaskCompletionSource<RunnerStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _pendingState = pending;
                _requestedStrategy = strategy;
            }

            try
            {
                await SendAsync(new HelperCommand(HelperProtocol.StartCommand, strategy.Id), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                FailAndReset($"Не удалось отправить команду запуска: {ex.Message}");
                throw new ZapretException($"Не удалось отправить команду запуска: {ex.Message}");
            }

            RunnerStatus status;
            try
            {
                status = await pending.Task.WaitAsync(CommandTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                FailAndReset("Helper не ответил на команду запуска.");
                throw new ZapretException("Helper не ответил на команду запуска.");
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _pendingState = null;
                }

                KillHelper();
                throw;
            }

            if (status.State != RunnerState.Running)
            {
                throw new ZapretException(status.Message ?? "Не удалось запустить zapret.");
            }

            lock (_sync)
            {
                _activeStrategy = strategy;
            }
        }
        finally
        {
            lock (_sync)
            {
                _pendingState = null;
            }

            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return;
            }

            lock (_sync)
            {
                if (!IsHelperAliveLocked())
                {
                    _state = RunnerState.Stopped;
                    _activeStrategy = null;
                    return;
                }

                if (_state == RunnerState.Stopped)
                {
                    return;
                }
            }

            var pending = new TaskCompletionSource<RunnerStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _pendingState = pending;
                _requestedStrategy = null;
            }

            try
            {
                await SendAsync(new HelperCommand(HelperProtocol.StopCommand), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                FailAndReset($"Не удалось отправить команду остановки: {ex.Message}");
                throw new ZapretException($"Не удалось отправить команду остановки: {ex.Message}");
            }

            RunnerStatus status;
            try
            {
                status = await pending.Task.WaitAsync(CommandTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                FailAndReset("Helper не ответил на команду остановки.");
                throw new ZapretException("Helper не ответил на команду остановки.");
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _pendingState = null;
                }

                KillHelper();
                throw;
            }

            if (status.State == RunnerState.Failed)
            {
                throw new ZapretException(status.Message ?? "Не удалось остановить zapret.");
            }
        }
        finally
        {
            lock (_sync)
            {
                _pendingState = null;
            }

            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Process? process;
            StreamWriter? input;
            lock (_sync)
            {
                process = _process;
                input = _input;
                _process = null;
                _input = null;
                _authenticated = false;
                _state = RunnerState.Stopped;
                _activeStrategy = null;
                _requestedStrategy = null;
            }

            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited && input is not null)
                {
                    var command = JsonSerializer.Serialize(
                        new HelperCommand(HelperProtocol.ShutdownCommand),
                        HelperProtocol.JsonOptions);
                    await input.WriteLineAsync(command);
                    await input.FlushAsync();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException)
            {
                // Fall through to closing stdin and killing the process.
            }

            try
            {
                if (!process.HasExited)
                {
                    input?.Close();
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }

            try
            {
                if (!process.HasExited && !process.WaitForExit(2000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private bool IsHelperAliveLocked()
    {
        if (!_authenticated || _process is null)
        {
            return false;
        }

        try
        {
            return !_process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task MonitorStdoutAsync(Process process, TaskCompletionSource<AuthFailure?> auth)
    {
        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                HelperMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<HelperMessage>(line, HelperProtocol.JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (message?.Type is null)
                {
                    continue;
                }

                switch (message.Type)
                {
                    case HelperProtocol.Ready:
                        HandleReady(process, auth);
                        break;

                    case HelperProtocol.State:
                        HandleState(message.State, message.Message);
                        break;

                    case HelperProtocol.Log:
                        if (!string.IsNullOrEmpty(message.Message))
                        {
                            LogReceived?.Invoke(message.Message);
                        }

                        break;

                    case HelperProtocol.Error:
                        HandleError(message.Message, auth);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private void HandleReady(Process process, TaskCompletionSource<AuthFailure?> auth)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_process, process))
            {
                return;
            }

            _authenticated = true;
            _state = RunnerState.Stopped;
        }

        auth.TrySetResult(null);
    }

    private void HandleState(string? stateName, string? message)
    {
        if (!Enum.TryParse<RunnerState>(stateName, ignoreCase: true, out var state))
        {
            return;
        }

        TaskCompletionSource<RunnerStatus>? pending = null;
        var status = new RunnerStatus(state, message);

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _state = state;

            if (state == RunnerState.Running)
            {
                _activeStrategy = _requestedStrategy;
            }
            else if (state is RunnerState.Stopped or RunnerState.Failed)
            {
                _activeStrategy = null;
            }

            if (state is RunnerState.Running or RunnerState.Stopped or RunnerState.Failed)
            {
                pending = _pendingState;
                _pendingState = null;
                _requestedStrategy = null;
            }
        }

        StatusChanged?.Invoke(status);
        pending?.TrySetResult(status);
    }

    private void HandleError(string? message, TaskCompletionSource<AuthFailure?> auth)
    {
        var text = string.IsNullOrWhiteSpace(message) ? "Helper сообщил об ошибке." : message!;

        if (!auth.Task.IsCompleted)
        {
            auth.TrySetResult(new AuthFailure(text, CanRetry: false));
            return;
        }

        TaskCompletionSource<RunnerStatus>? pending;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _state = RunnerState.Failed;
            _activeStrategy = null;
            _requestedStrategy = null;
            pending = _pendingState;
            _pendingState = null;
        }

        var status = new RunnerStatus(RunnerState.Failed, text);
        LogReceived?.Invoke(text);
        StatusChanged?.Invoke(status);
        pending?.TrySetResult(status);
    }

    private async Task MonitorStderrAsync(
        Process process,
        TaskCompletionSource<AuthFailure?> auth,
        TaskCompletionSource<bool> prompt)
    {
        var scan = new StringBuilder();
        var all = new StringBuilder();
        var promptSignaled = false;

        try
        {
            var buffer = new char[1024];
            while (true)
            {
                var read = await process.StandardError.ReadAsync(buffer.AsMemory());
                if (read <= 0)
                {
                    break;
                }

                var chunk = new string(buffer, 0, read);
                all.Append(chunk);
                if (all.Length > 16_000)
                {
                    all.Remove(0, all.Length - 12_000);
                }

                lock (_sync)
                {
                    _stderrTail = all.ToString();
                }

                scan.Append(chunk);
                while (true)
                {
                    var text = scan.ToString();
                    var markerIndex = text.IndexOf(SudoDiagnostics.PromptMarker, StringComparison.Ordinal);
                    if (markerIndex < 0)
                    {
                        break;
                    }

                    scan.Remove(0, markerIndex + SudoDiagnostics.PromptMarker.Length);
                    if (!promptSignaled)
                    {
                        promptSignaled = true;
                        prompt.TrySetResult(true);
                    }
                    else
                    {
                        auth.TrySetResult(new AuthFailure(
                            "Неверный пароль администратора.",
                            CanRetry: true));
                    }
                }

                if (scan.Length > 8192)
                {
                    scan.Remove(0, scan.Length - 4096);
                }

                if (!auth.Task.IsCompleted)
                {
                    var failure = SudoDiagnostics.TryMap(all.ToString());
                    if (failure is not null)
                    {
                        auth.TrySetResult(failure);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private async Task MonitorExitAsync(Process process, TaskCompletionSource<AuthFailure?> auth)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return;
        }

        if (!auth.Task.IsCompleted)
        {
            // Let the stderr reader drain sudo's final message (wrong password,
            // sudoers error) before building a generic exit-code diagnostic.
            await Task.Delay(100);
            var failure = SudoDiagnostics.BuildAuthFailure(process.ExitCode, GetStderrTail());
            auth.TrySetResult(failure);
        }

        HandleHelperExited(process);
        process.Dispose();
    }

    private void HandleHelperExited(Process process)
    {
        TaskCompletionSource<RunnerStatus>? pending;
        var status = new RunnerStatus(
            RunnerState.Failed,
            "Служба повышения прав завершилась. Включите защиту ещё раз.");

        lock (_sync)
        {
            if (_disposed
                || !_authenticated
                || !ReferenceEquals(_process, process))
            {
                return;
            }

            _authenticated = false;
            _process = null;
            _input = null;
            _state = RunnerState.Failed;
            _activeStrategy = null;
            _requestedStrategy = null;
            pending = _pendingState;
            _pendingState = null;
        }

        LogReceived?.Invoke(status.Message!);
        StatusChanged?.Invoke(status);
        pending?.TrySetResult(status);
    }

    private static async Task AnswerPasswordAsync(
        Task promptTask,
        Task<AuthFailure?> authTask,
        StreamWriter input,
        string password)
    {
        Task completed;
        try
        {
            completed = await Task.WhenAny(promptTask, authTask);
        }
        catch
        {
            return;
        }

        if (completed != promptTask)
        {
            // NOPASSWD: sudo started the helper without asking for a password.
            return;
        }

        try
        {
            await input.WriteLineAsync(password);
            await input.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The auth wait will report the process exit.
        }
    }

    private async Task SendAsync(HelperCommand command, CancellationToken cancellationToken)
    {
        StreamWriter? input;
        lock (_sync)
        {
            input = _input;
        }

        if (input is null)
        {
            throw new IOException("Канал helper не открыт.");
        }

        var json = JsonSerializer.Serialize(command, HelperProtocol.JsonOptions);
        await input.WriteLineAsync(json.AsMemory(), cancellationToken);
        await input.FlushAsync(cancellationToken);
    }

    private void FailAndReset(string message)
    {
        TaskCompletionSource<RunnerStatus>? pending;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _state = RunnerState.Failed;
            _activeStrategy = null;
            _requestedStrategy = null;
            pending = _pendingState;
            _pendingState = null;
        }

        var status = new RunnerStatus(RunnerState.Failed, message);
        LogReceived?.Invoke(message);
        StatusChanged?.Invoke(status);
        pending?.TrySetResult(status);
        KillHelper();
    }

    private void KillHelper()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
            _input = null;
            _authenticated = false;
            _requestedStrategy = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }

        try
        {
            if (process.WaitForExit(2000))
            {
                process.Dispose();
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private string GetStderrTail()
    {
        lock (_sync)
        {
            return _stderrTail;
        }
    }

    private static (string Executable, IReadOnlyList<string> Arguments) BuildHelperCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new AuthenticationFailedException(
                "Не удалось определить путь к исполняемому файлу приложения.",
                canRetry: false);
        }

        processPath = Path.GetFullPath(processPath);
        var arguments = new List<string>();

        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assembly))
            {
                throw new AuthenticationFailedException(
                    "Не удалось определить сборку приложения для запуска helper-процесса.",
                    canRetry: false);
            }

            arguments.Add(assembly);
        }

        arguments.Add(PrivilegedHelper.CommandArgument);
        return (processPath, arguments);
    }

    private static ProcessStartInfo BuildSudoStartInfo(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sudo",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";

        startInfo.ArgumentList.Add("-k");
        startInfo.ArgumentList.Add("-S");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(SudoDiagnostics.PromptMarker);

        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
