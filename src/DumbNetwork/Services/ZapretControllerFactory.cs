using DumbNetwork.Services.Firewall;

namespace DumbNetwork.Services;

public static class ZapretControllerFactory
{
    /// <summary>
    /// Linux non-root uses the password-authenticated helper. Windows always
    /// starts elevated through its manifest, and a root Linux session can use
    /// the local runner directly (no password dialog).
    /// </summary>
    public static IZapretController Create()
    {
        if (OperatingSystem.IsLinux() && !PrivilegeService.IsElevated())
        {
            return new PrivilegedHelperClient();
        }

        return new ZapretRunner(FirewallFactory.Create());
    }
}
