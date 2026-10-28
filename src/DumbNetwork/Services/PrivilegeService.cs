using System.Runtime.InteropServices;

namespace DumbNetwork.Services;

public static class PrivilegeService
{
    public static bool IsElevated()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsUserAnAdmin();
        }

        if (OperatingSystem.IsLinux())
        {
            return GetEuid() == 0;
        }

        return false;
    }

    public static string RequirementHint => OperatingSystem.IsWindows()
        ? "Запустите приложение от имени администратора (UAC). winws загружает драйвер WinDivert."
        : "nfqws и nftables требуют root. При включении приложение запросит пароль администратора.";

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsUserAnAdmin();
}
