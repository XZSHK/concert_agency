using Avalon.Core;
using Xunit;

namespace Avalon.Tests;

/// <summary>
/// Тест 1 (ПК 1.4): проверка математической формулы расчёта позиции
/// и итоговой суммы заказа при стандартных параметрах.
/// Стандарт: 2×Партер по 1500 + 1×VIP по 3000 (бейдж +500) = 6500,
/// сервисный сбор 10% = 650, итого 7150.
/// </summary>
public class OrderCalculatorTests
{
    [Fact]
    public void Test1_StandardOrderCalculation()
    {
        var totals = OrderCalculator.Calculate(
            parterreQty: 2, vipQty: 1,
            parterrePrice: 1500m, vipPrice: 3000m,
            hasPromoSubscription: false);

        Assert.Equal(1500m, totals.ParterreFinalPrice);
        Assert.Equal(3500m, totals.VipFinalPrice); // 3000 + 500 бейдж
        Assert.Equal(6500m, totals.Subtotal);      // 2*1500 + 1*3500
        Assert.Equal(650m, totals.ServiceFee);     // 10% от 6500
        Assert.Equal(7150m, totals.Total);
        Assert.Equal(3, totals.SeatsCount);
    }

    [Fact]
    public void Test1_SingleParterreSeat()
    {
        var totals = OrderCalculator.Calculate(1, 0, 2000m, 4000m, false);

        Assert.Equal(2000m, totals.Subtotal);
        Assert.Equal(200m, totals.ServiceFee);
        Assert.Equal(2200m, totals.Total);
    }

    /// <summary>
    /// Тест 2 (ПК 1.4): корректность скидок/льгот/надбавок.
    /// Промокод подписчика обнуляет сервисный сбор;
    /// VIP включает надбавку +500 руб. за бейдж.
    /// </summary>
    [Fact]
    public void Test2_PromoSubscriptionZeroesServiceFee()
    {
        var totals = OrderCalculator.Calculate(
            parterreQty: 2, vipQty: 1,
            parterrePrice: 1500m, vipPrice: 3000m,
            hasPromoSubscription: true);

        Assert.Equal(6500m, totals.Subtotal);
        Assert.Equal(0m, totals.ServiceFee); // льгота подписчика
        Assert.Equal(6500m, totals.Total);
    }

    [Fact]
    public void Test2_VipBadgeSurcharge()
    {
        Assert.Equal(3500m, OrderCalculator.FinalSeatCost("VIP", 3000m));
        Assert.Equal(1500m, OrderCalculator.FinalSeatCost("Партер", 1500m));
        Assert.Equal(3500m, OrderCalculator.FinalSeatCost("vip", 3000m)); // регистронезависимо
    }

    [Fact]
    public void Test2_ProducerFeeIs30PercentOfPotential()
    {
        decimal potential = OrderCalculator.PotentialRevenue(1500m, 800, 3000m, 100);
        Assert.Equal(1_500_000m, potential); // 1500*800 + 3000*100
        Assert.Equal(450_000m, OrderCalculator.CalculateProducerFee(potential));
    }
}
