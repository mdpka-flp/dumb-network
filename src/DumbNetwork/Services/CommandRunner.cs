using System.Diagnostics;
using System.Text;

namespace DumbNetwork.Services;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    public string Describe()
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(StandardOutput))
        {
            builder.AppendLine(StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(StandardError))
        {
            builder.AppendLine(StandardError.Trim());
        }

        return builder.ToString().Trim();
    }
}

/// <summary>Small wrapper around <see cref="Process"/> for short-lived helper commands.</summary>
public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);

            return new CommandResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new CommandResult(127, string.Empty, ex.Message);
        }
    }

    public static bool ExistsOnPath(string executableName)
    {
        if (Path.IsPathRooted(executableName))
        {
            return File.Exists(executableName);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executableName))
            .Any(File.Exists);
    }

    public static string FormatCommandLine(string fileName, IEnumerable<string> arguments)
    {
        return string.Join(
            ' ',
            new[] { Quote(fileName) }.Concat(arguments.Select(Quote)));
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
