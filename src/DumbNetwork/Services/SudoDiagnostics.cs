namespace DumbNetwork.Services;

public sealed record AuthFailure(string Message, bool CanRetry);

/// <summary>
/// Maps the English stderr produced by <c>sudo</c> (we launch it with
/// <c>LANG=C</c> for stable diagnostics) to user-facing Russian messages.
/// </summary>
public static class SudoDiagnostics
{
    public const string PromptMarker = "__DUMBNET_SUDO_PASSWORD__";

    public static AuthFailure? TryMap(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        var text = stderr.Replace(PromptMarker, string.Empty, StringComparison.Ordinal);

        if (Contains(text, "incorrect password") || Contains(text, "sorry, try again"))
        {
            return new AuthFailure("Неверный пароль администратора.", CanRetry: true);
        }

        if (Contains(text, "not in the sudoers file") || Contains(text, "is not allowed to execute"))
        {
            return new AuthFailure(
                "Пользователь не имеет прав на запуск приложения через sudo. Проверьте настройки sudoers.",
                CanRetry: false);
        }

        if (Contains(text, "a terminal is required") || Contains(text, "no tty present"))
        {
            return new AuthFailure(
                "sudo требует терминал для ввода пароля. Разрешите работу без TTY (Defaults !requiretty) "
                + "или запустите приложение из терминала.",
                CanRetry: false);
        }

        if (Contains(text, "no password was provided") || Contains(text, "a password is required"))
        {
            return new AuthFailure("sudo не получил пароль. Попробуйте ещё раз.", CanRetry: true);
        }

        if (Contains(text, "command not found"))
        {
            return new AuthFailure("Не удалось найти sudo. Установите sudo и повторите попытку.", CanRetry: false);
        }

        return null;
    }

    public static AuthFailure BuildAuthFailure(int exitCode, string? stderr)
    {
        var mapped = TryMap(stderr);
        if (mapped is not null)
        {
            return mapped;
        }

        var detail = Normalize(stderr);
        var suffix = detail.Length == 0 ? string.Empty : $"\n{detail}";
        return new AuthFailure(
            $"Не удалось получить права root (код {exitCode}).{suffix}",
            CanRetry: false);
    }

    private static bool Contains(string text, string value)
        => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return string.Empty;
        }

        var lines = stderr
            .Replace(PromptMarker, string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToArray();

        var text = string.Join(" ", lines);
        if (text.Length > 500)
        {
            text = text[..500] + "…";
        }

        return text;
    }
}
