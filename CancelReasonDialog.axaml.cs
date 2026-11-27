using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Avalon;

/// <summary>Модальный диалог причины отмены документа (cancel_reason, минимум 10 символов — по ТЗ).</summary>
public partial class CancelReasonDialog : Window
{
    public const int MinReasonLength = 10;

    public CancelReasonDialog()
    {
        InitializeComponent();
    }

    private void TxtReason_TextChanged(object? sender, TextChangedEventArgs e)
    {
        int length = TxtReason.Text?.Trim().Length ?? 0;
        TxtCounter.Text = $"{length} / мин. {MinReasonLength}";
        TxtCounter.Foreground = length >= MinReasonLength
            ? Avalonia.Media.Brushes.LightGreen
            : Avalonia.Media.Brushes.Orange;
        BtnConfirm.IsEnabled = length >= MinReasonLength;
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        string? reason = TxtReason.Text?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < MinReasonLength)
        {
            BtnConfirm.IsEnabled = false;
            return;
        }
        Close(reason);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
