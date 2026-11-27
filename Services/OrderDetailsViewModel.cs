using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalon.Core;

namespace Avalon.Services;

/// <summary>Строка спецификации документа (позиция заказа).</summary>
public sealed class SeatLineItem
{
    public SeatLineItem(OrderService.OrderSeatLine line)
    {
        SeatId = line.SeatId;
        SectorType = line.SectorType;
        RowNum = line.RowNum;
        SeatNum = line.SeatNum;
        BaseSeatPrice = line.BaseSeatPrice;
        FinalSeatCost = line.FinalSeatCost;
    }

    public long SeatId { get; }
    public string SectorType { get; }
    public int RowNum { get; }
    public int SeatNum { get; }
    public decimal BaseSeatPrice { get; }
    public decimal FinalSeatCost { get; }
}

/// <summary>
/// ViewModel окна «Документ» (Master-Detail): заголовок + строки спецификации.
/// Итоги документа пересчитываются через механизмы привязки (INPC) при любом
/// изменении коллекции позиций.
/// </summary>
public sealed class OrderDetailsViewModel : INotifyPropertyChanged
{
    private decimal _serviceFee;

    public OrderDetailsViewModel(OrderService.OrderHeaderInfo header, IEnumerable<OrderService.OrderSeatLine> seats)
    {
        Header = header;
        Seats.CollectionChanged += Seats_CollectionChanged;
        foreach (var line in seats)
            Seats.Add(new SeatLineItem(line));
        ServiceFee = header.ServiceFee;
        RaiseSeatsChanged();
    }

    public OrderService.OrderHeaderInfo Header { get; }

    public ObservableCollection<SeatLineItem> Seats { get; } = new();

    public string OrderRef => Header.OrderRef;
    public DateTime OrderDate => Header.OrderDate;
    public string Status => Header.Status;
    public string BuyerName => Header.BuyerName;
    public string ArtistName => Header.ArtistName;
    public DateTime EventDate => Header.EventDate;
    public string ResponsibleName => Header.ResponsibleName ?? "— (онлайн-заказ)";
    public string? CancelReason => Header.CancelReason;
    public bool HasCancelReason => !string.IsNullOrWhiteSpace(CancelReason);

    /// <summary>Редактирование позиций разрешено только в статусе «Бронь (15 мин)» (ТЗ: оплаченные и закрытые документы не редактируются).</summary>
    public bool IsEditable => Status == OrderStatus.Booking;

    // ---------- Итоги (привязка, пересчёт на лету) ----------
    public int SeatsCount => Seats.Count;
    public decimal SeatsTotal => Seats.Sum(s => s.FinalSeatCost);

    public decimal ServiceFee
    {
        get => _serviceFee;
        set
        {
            if (_serviceFee == value) return;
            _serviceFee = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Total));
        }
    }

    public decimal Total => SeatsTotal + ServiceFee;

    // ---------- INPC ----------
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Seats_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseSeatsChanged();

    public void RaiseSeatsChanged()
    {
        OnPropertyChanged(nameof(SeatsCount));
        OnPropertyChanged(nameof(SeatsTotal));
        OnPropertyChanged(nameof(Total));
    }

    public void SetServiceFee(decimal fee) => ServiceFee = fee;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
