using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using DumbNetwork.ViewModels;

namespace DumbNetwork.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeToViewModel();
    }

    public async Task<string?> PromptForPasswordAsync(string? error)
    {
        var dialog = new PasswordDialog(error);
        return await dialog.ShowDialog<string?>(this);
    }

    private void SubscribeToViewModel()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(MainWindowViewModel.LogText))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose && DataContext is MainWindowViewModel viewModel)
        {
            e.Cancel = true;
            _allowClose = true;

            try
            {
                await viewModel.ShutdownAsync();
            }
            finally
            {
                Close();
            }

            return;
        }

        base.OnClosing(e);
    }
}
