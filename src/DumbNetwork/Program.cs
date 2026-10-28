using Avalonia;
using System;
using DumbNetwork.Services;

namespace DumbNetwork;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // The privileged helper is the same executable, launched by sudo from the
        // unprivileged GUI. It must not initialize Avalonia or create a window.
        if (args.Length > 0
            && string.Equals(args[0], PrivilegedHelper.CommandArgument, StringComparison.Ordinal))
        {
            return PrivilegedHelper.RunAsync().GetAwaiter().GetResult();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
