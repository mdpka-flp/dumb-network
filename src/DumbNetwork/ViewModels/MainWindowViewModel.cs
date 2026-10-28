using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DumbNetwork.Services;
using DumbNetwork.Strategies;

namespace DumbNetwork.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IZapretController _controller;
    private readonly StringBuilder _log = new();
    private bool _shutdownRequested;

    public MainWindowViewModel() : this(ZapretControllerFactory.Create())
    {
    }

    public MainWindowViewModel(IZapretController controller)
    {
        _controller = controller;
        _controller.LogReceived += OnControllerLog;
        _controller.StatusChanged += OnControllerStatusChanged;

        var isWindows = OperatingSystem.IsWindows();
        Strategies = new ObservableCollection<StrategyDefinition>(
            StrategyCatalog.ForPlatform(isWindows));
        SelectedStrategy = Strategies.FirstOrDefault();
        ActiveStrategyName = SelectedStrategy?.Name ?? "—";

        PlatformText = $"{(isWindows ? "Windows" : "Linux")} · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        IsElevated = PrivilegeService.IsElevated();
        PrivilegeText = IsElevated
            ? (isWindows ? "Права: администратор" : "Права: root")
            : (isWindows ? "Права: нет (нужен администратор)" : "Права: не подтверждены (sudo)");

        if (BackendLayout.TryLocate(out var layout, out var backendError))
        {
            BackendAvailable = true;
            BackendText = layout!.DisplayName;
            AppendLog($"Бэкенд найден: {layout.RootDirectory}");
        }
        else
        {
            BackendAvailable = false;
            BackendText = "Бэкенд не найден";
            IsFailed = true;
            StatusText = "НЕТ БЭКЕНДА";
            StatusDetails = backendError ?? "Запустите ./scripts/fetch-zapret.sh";
            AppendLog(StatusDetails);
        }

        if (!IsElevated)
        {
            AppendLog(PrivilegeService.RequirementHint);
            if (!IsFailed)
            {
                StatusDetails = isWindows
                    ? PrivilegeService.RequirementHint
                    : "При включении потребуется пароль администратора (sudo).";
            }
        }
    }

    public ObservableCollection<StrategyDefinition> Strategies { get; }

    public string PlatformText { get; }

    public string BackendText { get; }

    public bool BackendAvailable { get; }

    public bool IsElevated { get; }

    /// <summary>
    /// Set by the view layer. Returns the entered password, or null when the
    /// user cancels. The error parameter is shown inside the dialog on retry.
    /// </summary>
    public Func<string?, Task<string?>>? PasswordPrompt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleHint))]
    private bool isRunning;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isFailed;

    [ObservableProperty]
    private string statusText = "ВЫКЛЮЧЕНО";

    [ObservableProperty]
    private string statusDetails = "Выберите стратегию и нажмите кнопку питания.";

    [ObservableProperty]
    private string logText = string.Empty;

    [ObservableProperty]
    private string privilegeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleHint))]
    private string activeStrategyName = "—";

    [ObservableProperty]
    private StrategyDefinition? selectedStrategy;

    public string ToggleHint => IsRunning
        ? "Нажмите, чтобы выключить"
        : "Нажмите, чтобы включить";

    public bool CanToggle => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        if (_controller.State == RunnerState.Running && !_controller.NeedsAuthentication)
        {
            await _controller.StopAsync();
            return;
        }

        if (SelectedStrategy is null)
        {
            StatusDetails = "Сначала выберите стратегию в левом меню.";
            return;
        }

        try
        {
            if (_controller.NeedsAuthentication)
            {
                IsBusy = true;
                bool authenticated;
                try
                {
                    authenticated = await EnsureAuthenticatedAsync();
                }
                finally
                {
                    IsBusy = false;
                }

                if (!authenticated)
                {
                    return;
                }
            }

            await _controller.StartAsync(SelectedStrategy);
        }
        catch (Exception ex)
        {
            // The controller has already updated the status and written the reason to the log.
            AppendLog($"Запуск отменён: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        _log.Clear();
        LogText = string.Empty;
    }

    public async Task ShutdownAsync()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;

        try
        {
            if (_controller.State != RunnerState.Stopped)
            {
                await _controller.StopAsync();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Ошибка при остановке: {ex.Message}");
        }

        _controller.LogReceived -= OnControllerLog;
        _controller.StatusChanged -= OnControllerStatusChanged;
        await _controller.DisposeAsync();
    }

    partial void OnSelectedStrategyChanged(StrategyDefinition? value)
    {
        if (!IsRunning)
        {
            ActiveStrategyName = value?.Name ?? "—";
        }

        if (value is not null)
        {
            AppendLog($"Выбрана стратегия: {value.Name}");
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        ToggleCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        ToggleCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (PasswordPrompt is null)
        {
            StatusDetails = "Диалог ввода пароля недоступен.";
            AppendLog(StatusDetails);
            return false;
        }

        string? error = null;
        while (true)
        {
            string? password;
            try
            {
                password = await PasswordPrompt(error);
            }
            catch (Exception ex)
            {
                StatusDetails = ex.Message;
                AppendLog($"Не удалось открыть диалог пароля: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(password))
            {
                StatusDetails = "Включение отменено.";
                AppendLog("Ввод пароля отменён.");
                return false;
            }

            try
            {
                await _controller.AuthenticateAsync(password);
            }
            catch (AuthenticationFailedException ex)
            {
                error = ex.Message;
                StatusDetails = ex.Message;
                AppendLog(ex.Message);

                if (!ex.CanRetry)
                {
                    return false;
                }

                continue;
            }
            catch (Exception ex)
            {
                StatusDetails = ex.Message;
                AppendLog($"Не удалось получить права root: {ex.Message}");
                return false;
            }

            PrivilegeText = "Права: root (подтверждено)";
            AppendLog("Права root подтверждены.");
            return true;
        }
    }

    private void OnControllerLog(string message)
    {
        Dispatcher.UIThread.Post(() => AppendLog(message));
    }

    private void OnControllerStatusChanged(RunnerStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsBusy = status.State is RunnerState.Starting or RunnerState.Stopping;
            IsRunning = status.State == RunnerState.Running;
            IsFailed = status.State == RunnerState.Failed;

            StatusText = status.State switch
            {
                RunnerState.Stopped => "ВЫКЛЮЧЕНО",
                RunnerState.Starting => "ЗАПУСК…",
                RunnerState.Running => "ЗАЩИТА ВКЛЮЧЕНА",
                RunnerState.Stopping => "ОСТАНОВКА…",
                RunnerState.Failed => "ОШИБКА",
                _ => StatusText,
            };

            if (!string.IsNullOrWhiteSpace(status.Message))
            {
                StatusDetails = status.Message!;
            }

            if (status.State == RunnerState.Running)
            {
                ActiveStrategyName = _controller.ActiveStrategy?.Name ?? ActiveStrategyName;
            }
            else if (status.State == RunnerState.Stopped)
            {
                ActiveStrategyName = SelectedStrategy?.Name ?? "—";
            }

            if (status.State == RunnerState.Failed && _controller.NeedsAuthentication)
            {
                PrivilegeText = "Права: не подтверждены (sudo)";
            }
        });
    }

    private void AppendLog(string message)
    {
        _log.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").AppendLine(message);
        if (_log.Length > 24_000)
        {
            _log.Remove(0, _log.Length - 20_000);
        }

        LogText = _log.ToString();
    }
}
