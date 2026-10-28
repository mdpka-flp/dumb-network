using System.Text.Json;
using System.Text.Json.Serialization;

namespace DumbNetwork.Services;

/// <summary>
/// JSON-lines protocol between the GUI and the root helper. The helper accepts
/// only strategy IDs from <c>StrategyCatalog</c>; arbitrary commands/arguments
/// are never executed, so the channel cannot be used as a generic root shell.
/// </summary>
internal static class HelperProtocol
{
    public const string Ready = "ready";
    public const string State = "state";
    public const string Log = "log";
    public const string Error = "error";

    public const string StartCommand = "start";
    public const string StopCommand = "stop";
    public const string ShutdownCommand = "shutdown";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record HelperMessage(string Type, string? State = null, string? Message = null);

internal sealed record HelperCommand(string Command, string? StrategyId = null);
