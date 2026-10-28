using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DumbNetwork.Views;

public partial class PasswordDialog : Window
{
    private readonly string? _initialError;

    public PasswordDialog() : this(null)
    {
    }

    public PasswordDialog(string? initialError)
    {
        InitializeComponent();
        _initialError = initialError;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(_initialError))
        {
            ShowError(_initialError);
        }

        PasswordBox.Focus();
        PasswordBox.CaretIndex = PasswordBox.Text?.Length ?? 0;
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        // Do not keep the entered password alive in the dialog instance.
        PasswordBox.Text = string.Empty;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs eventArgs)
    {
        Close((string?)null);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs eventArgs)
    {
        Confirm();
    }

    private void Confirm()
    {
        var password = PasswordBox.Text;
        if (string.IsNullOrEmpty(password))
        {
            ShowError("Введите пароль администратора.");
            PasswordBox.Focus();
            return;
        }

        Close(password);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
