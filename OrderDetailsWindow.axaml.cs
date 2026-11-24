using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using Avalon.Services;

namespace Avalon;

/// <summary>
/// Экран «Документ» (Master-Detail): шапка заказа + строки спецификации.
/// Редактирование позиций разрешено только в статусе «Бронь (15 мин)» (по ТЗ);
/// оплаченные и закрытые документы открываются только для просмотра.
/// </summary>
public partial class OrderDetailsWindow : Window
{
    private readonly long _orderId;
    private OrderDetailsViewModel _viewModel = null!;

    public OrderDetailsWindow(long orderId)
    {
        InitializeComponent();
        _orderId = orderId;
        Reload();
    }

    /// <summary>Перечитать данные из БД и обновить привязки (итоги пересчитываются сами).</summary>
    private void Reload()
    {
        var header = OrderService.LoadHeader(_orderId);
        var seats = OrderService.LoadSeats(_orderId);

        if (_viewModel == null)
        {
            _viewModel = new OrderDetailsViewModel(header, seats);
            DataContext = _viewModel;
        }
        else
        {
            _viewModel.Seats.Clear();
            foreach (var line in seats)
                _viewModel.Seats.Add(new SeatLineItem(line));
            _viewModel.SetServiceFee(header.ServiceFee);
        }
    }

    private void BtnAddParterre_Click(object? sender, RoutedEventArgs e) => AddSeat("Партер");
    private void BtnAddVip_Click(object? sender, RoutedEventArgs e) => AddSeat("VIP");

    private void AddSeat(string sector)
    {
        try
        {
            HideMessage();
            OrderService.AddSeat(_orderId, sector);
            Reload();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    private async void BtnDeleteSeat_Click(object? sender, RoutedEventArgs e)
    {
        if (GridSeats.SelectedItem is not SeatLineItem selected)
        {
            ShowMessage("Выберите позицию в таблице.");
            return;
        }

        // Модальное подтверждение удаления (ТЗ).
        var confirm = new ConfirmDialog(
            $"Удалить позицию «{selected.SectorType}», ряд {selected.RowNum}, место {selected.SeatNum}?");
        bool ok = await confirm.ShowDialog<bool>(this);
        if (!ok) return;

        try
        {
            HideMessage();
            OrderService.RemoveSeat(selected.SeatId);
            Reload();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    private void ShowMessage(string text)
    {
        TxtMessage.Text = text;
        TxtMessage.IsVisible = true;
    }

    private void HideMessage()
    {
        TxtMessage.IsVisible = false;
        TxtMessage.Text = "";
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();
}
