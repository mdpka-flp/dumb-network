using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DumbNetwork.ViewModels;
using DumbNetwork.Views;

namespace DumbNetwork;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var window = new MainWindow
            {
                DataContext = viewModel,
            };

            // The password dialog belongs to the view layer; the view model only
            // knows the callback. It is invoked only if Linux needs a root helper.
            viewModel.PasswordPrompt = window.PromptForPasswordAsync;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
