using System.Text;
using System.Text.Json;
using DumbNetwork.Services.Firewall;
using DumbNetwork.Strategies;

namespace DumbNetwork.Services;

/// <summary>
/// Root-side process. It is launched by <see cref="PrivilegedHelperClient"/> through
/// <c>sudo</c> and speaks JSON lines over stdin/stdout.
///
/// The helper deliberately accepts only known strategy IDs. It never takes a
/// command line or arbitrary nfqws arguments from the unprivileged side.
/// </summary>
public static class PrivilegedHelper
{
    public const string CommandArgument = "--privileged-helper";

    public static async Task<int> RunAsync()
    {
        var outputLock = new object();
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };

        void Send(string type, string? state = null, string? message = null)
        {
            var messageJson = JsonSerializer.Serialize(
                new HelperMessage(type, state, message),
                HelperProtocol.JsonOptions);

            lock (outputLock)
            {
                try
                {
                    output.WriteLine(messageJson);
                    output.Flush();
                }
                catch (IOException)
                {
                    // The GUI is gone; the read loop will see EOF soon.
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        if (!PrivilegeService.IsElevated())
        {
            Send(HelperProtocol.Error, message: "Для helper-процесса нужны права root.");
            return 2;
        }

        await using var runner = new ZapretRunner(FirewallFactory.Create());
        runner.LogReceived += message => Send(HelperProtocol.Log, message: message);
        runner.StatusChanged += status => Send(
            HelperProtocol.State,
            status.State.ToString().ToLowerInvariant(),
            status.Message);

        Send(HelperProtocol.Ready);

        var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        try
        {
            while (true)
            {
                var line = await input.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                HelperCommand? command;
                try
                {
                    command = JsonSerializer.Deserialize<HelperCommand>(line, HelperProtocol.JsonOptions);
                }
                catch (JsonException)
                {
                    Send(HelperProtocol.Error, message: "Некорректная команда helper.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(command?.Command))
                {
                    Send(HelperProtocol.Error, message: "Пустая команда helper.");
                    continue;
                }

                switch (command.Command)
                {
                    case HelperProtocol.StartCommand:
                        await StartAsync(runner, command.StrategyId, message => Send(HelperProtocol.Error, message: message));
                        break;

                    case HelperProtocol.StopCommand:
                        await StopAsync(runner, message => Send(HelperProtocol.Error, message: message));
                        break;

                    case HelperProtocol.ShutdownCommand:
                        await StopAsync(runner, message => Send(HelperProtocol.Error, message: message));
                        return 0;

                    default:
                        Send(HelperProtocol.Error, message: $"Неизвестная команда helper: {command.Command}.");
                        break;
                }
            }
        }
        finally
        {
            try
            {
                await runner.StopAsync();
            }
            catch
            {
                // Best effort: the caller may already be gone.
            }
        }

        return 0;
    }

    private static async Task StartAsync(
        ZapretRunner runner,
        string? strategyId,
        Action<string> sendError)
    {
        var strategy = StrategyCatalog
            .ForPlatform(isWindows: false)
            .FirstOrDefault(item => string.Equals(item.Id, strategyId, StringComparison.Ordinal));

        if (strategy is null)
        {
            sendError($"Неизвестная стратегия: {strategyId ?? "<пусто>"}.");
            return;
        }

        try
        {
            await runner.StartAsync(strategy);
        }
        catch (Exception ex)
        {
            sendError(ex.Message);
        }
    }

    private static async Task StopAsync(ZapretRunner runner, Action<string> sendError)
    {
        try
        {
            await runner.StopAsync();
        }
        catch (Exception ex)
        {
            sendError(ex.Message);
        }
    }
}
