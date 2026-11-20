using System;

namespace Avalon.Core;

/// <summary>
/// Чистая математика заказа по ТЗ (без БД и UI — покрывается модульными тестами, ПК 1.4).
///
/// Формулы ТЗ:
///   final_seat_cost = base_seat_price + (сектор VIP ? 500 руб. бейдж : 0)
///   service_fee     = has_promo_subscription ? 0 : 10% от стоимости заказа
///   total_amount    = сумма(final_seat_cost) + service_fee
/// </summary>
public static class OrderCalculator
{
    /// <summary>Надбавка за сувенирный бейдж в секторе VIP, руб.</summary>
    public const decimal VipBadgeSurcharge = 500m;

    /// <summary>Ставка сервисного сбора агентства.</summary>
    public const decimal ServiceFeeRate = 0.10m;

    /// <summary>Итоговая стоимость одного места с учётом бейджа VIP.</summary>
    public static decimal FinalSeatCost(string sectorType, decimal baseSeatPrice)
    {
        if (baseSeatPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(baseSeatPrice), "Цена места не может быть отрицательной.");

        return IsVip(sectorType) ? baseSeatPrice + VipBadgeSurcharge : baseSeatPrice;
    }

    public static bool IsVip(string sectorType) =>
        string.Equals(sectorType?.Trim(), "VIP", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Рассчитать суммы заказа.
    /// </summary>
    /// <param name="parterreQty">Количество мест в партере.</param>
    /// <param name="vipQty">Количество мест в VIP.</param>
    /// <param name="parterrePrice">Базовая цена партера (concert_events.parterre_price).</param>
    /// <param name="vipPrice">Базовая цена VIP (concert_events.vip_price, без бейджа).</param>
    /// <param name="hasPromoSubscription">Промокод подписчика обнуляет сервисный сбор.</param>
    public static OrderTotals Calculate(
        int parterreQty, int vipQty,
        decimal parterrePrice, decimal vipPrice,
        bool hasPromoSubscription)
    {
        if (parterreQty < 0) throw new ArgumentOutOfRangeException(nameof(parterreQty));
        if (vipQty < 0) throw new ArgumentOutOfRangeException(nameof(vipQty));
        if (parterreQty + vipQty == 0)
            throw new ArgumentException("Выберите хотя бы один билет.");

        decimal parterreFinal = FinalSeatCost("Партер", parterrePrice);
        decimal vipFinal = FinalSeatCost("VIP", vipPrice);

        decimal subtotal = parterreQty * parterreFinal + vipQty * vipFinal;
        decimal serviceFee = hasPromoSubscription
            ? 0m
            : Math.Round(subtotal * ServiceFeeRate, 2, MidpointRounding.AwayFromZero);
        decimal total = subtotal + serviceFee;

        return new OrderTotals(
            ParterreFinalPrice: parterreFinal,
            VipFinalPrice: vipFinal,
            Subtotal: subtotal,
            ServiceFee: serviceFee,
            Total: total,
            SeatsCount: parterreQty + vipQty);
    }

    /// <summary>
    /// Гонорар исполнителя для продюсера: 30% от потенциального сбора
    /// (полная заполняемость зала по ценам события).
    /// </summary>
    public static decimal CalculateProducerFee(decimal potentialRevenue) =>
        Math.Round(potentialRevenue * 0.30m, 2, MidpointRounding.AwayFromZero);

    public static decimal PotentialRevenue(
        decimal parterrePrice, int parterreCapacity,
        decimal vipPrice, int vipCapacity) =>
        parterrePrice * parterreCapacity + vipPrice * vipCapacity;
}

/// <summary>Результат расчёта заказа.</summary>
public sealed record OrderTotals(
    decimal ParterreFinalPrice,
    decimal VipFinalPrice,
    decimal Subtotal,
    decimal ServiceFee,
    decimal Total,
    int SeatsCount);
