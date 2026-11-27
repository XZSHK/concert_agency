using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Avalon;

/// <summary>Модальное подтверждение деструктивного действия (удаление позиции документа).</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string question) : this()
    {
        TxtQuestion.Text = question;
    }

    private void BtnYes_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void BtnNo_Click(object? sender, RoutedEventArgs e) => Close(false);
}
