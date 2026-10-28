using System.Runtime.InteropServices;

namespace DumbNetwork.Services;

public sealed class BackendNotFoundException : Exception
{
    public BackendNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>
/// Location of the bundled zapret binaries. Supports the source layout
/// (<c>backend/zapret/&lt;version&gt;/...</c>) and a custom root via the
/// <c>DUMBZAPRET_BACKEND</c> environment variable.
/// </summary>
public sealed record BackendLayout(
    string RootDirectory,
    string Version,
    string PlatformDirectory,
    string ExecutablePath,
    string FakeDirectory,
    string RuntimeIdentifier,
    bool IsWindows)
{
    public const string EnvironmentVariable = "DUMBZAPRET_BACKEND";

    public string DisplayName => $"zapret {Version} · {RuntimeIdentifier}";

    public static bool TryLocate(out BackendLayout? layout, out string? error)
    {
        try
        {
            layout = Locate();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is BackendNotFoundException or NotSupportedException)
        {
            layout = null;
            error = ex.Message;
            return false;
        }
    }

    public static BackendLayout Locate()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var executableName = OperatingSystem.IsWindows() ? "winws.exe" : "nfqws";
        var searched = new List<string>();

        foreach (var root in GetCandidateRoots().Distinct(StringComparer.Ordinal))
        {
            searched.Add(root);
            if (!Directory.Exists(root))
            {
                continue;
            }

            var versionDirectory = ResolveVersionDirectory(root);
            if (versionDirectory is null)
            {
                continue;
            }

            var platformDirectory = Path.Combine(versionDirectory, runtimeIdentifier);
            var executablePath = Path.Combine(platformDirectory, executableName);
            var fakeDirectory = Path.Combine(versionDirectory, "files", "fake");
            if (!File.Exists(executablePath) || !Directory.Exists(fakeDirectory))
            {
                continue;
            }

            EnsureExecutable(executablePath);

            var version = ReadVersion(versionDirectory);
            return new BackendLayout(
                versionDirectory,
                version,
                platformDirectory,
                executablePath,
                fakeDirectory,
                runtimeIdentifier,
                OperatingSystem.IsWindows());
        }

        var expected = Path.Combine(
            "backend", "zapret", "<version>", runtimeIdentifier, executableName);
        throw new BackendNotFoundException(
            $"Не найден бэкенд zapret ({runtimeIdentifier}). Ожидался файл '{expected}'. "
            + "Запустите ./scripts/fetch-zapret.sh или задайте DUMBZAPRET_BACKEND. "
            + $"Проверенные пути: {string.Join("; ", searched)}");
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return Path.GetFullPath(configured);
        }

        yield return Path.Combine(AppContext.BaseDirectory, "backend", "zapret");

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                yield return Path.Combine(directory.FullName, "backend", "zapret");
                directory = directory.Parent;
            }
        }
    }

    private static string? ResolveVersionDirectory(string root)
    {
        if (File.Exists(Path.Combine(root, "VERSION")))
        {
            return root;
        }

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, "VERSION")))
            .OrderByDescending(directory => ParseVersion(Path.GetFileName(directory)))
            .ThenByDescending(directory => Path.GetFileName(directory), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static System.Version ParseVersion(string name)
    {
        var trimmed = name.TrimStart('v', 'V');
        return System.Version.TryParse(trimmed, out var version) ? version : new System.Version(0, 0);
    }

    private static string ReadVersion(string versionDirectory)
    {
        var file = Path.Combine(versionDirectory, "VERSION");
        if (File.Exists(file))
        {
            var text = File.ReadAllText(file).Trim();
            if (text.Length > 0)
            {
                return text;
            }
        }

        return Path.GetFileName(versionDirectory);
    }

    private static string GetRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;

        if (OperatingSystem.IsWindows())
        {
            return architecture switch
            {
                Architecture.X64 => "windows-x64",
                _ => throw new NotSupportedException(
                    "Windows ARM64 не поддерживается: в zapret нет подписанного драйвера WinDivert для ARM64."),
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return architecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                Architecture.X86 => "linux-x86",
                Architecture.Arm => "linux-arm",
                _ => throw new NotSupportedException(
                    $"Архитектура Linux {architecture} не поддерживается бандлом zapret."),
            };
        }

        throw new NotSupportedException(
            "Поддерживаются только Linux и Windows. На macOS zapret работает иначе (pf), этот клиент его не настраивает.");
    }

    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & UnixFileMode.UserExecute) == 0)
            {
                File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute);
            }
        }
        catch (IOException)
        {
            // Best effort: the backend directory may be read-only.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
