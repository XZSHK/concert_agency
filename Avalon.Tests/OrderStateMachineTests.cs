using System;
using Avalon.Core;
using Xunit;

namespace Avalon.Tests;

/// <summary>
/// Тесты 3–4 (ПК 1.4): FSM жизненного цикла заказа.
/// </summary>
public class OrderStateMachineTests
{
    private static readonly DateTime EventDate = new(2026, 12, 10, 19, 0, 0);
    private static readonly DateTime LongBeforeEvent = new(2026, 11, 20, 12, 0, 0);

    /// <summary>
    /// Тест 3: успешный перевод заказа по разрешённой цепочке FSM:
    /// Бронь -> Оплачен -> Выписан -> Контроль на входе пройден.
    /// </summary>
    [Fact]
    public void Test3_AllowedChainTransitions()
    {
        string s1 = OrderStateMachine.Transition(OrderStatus.Booking, OrderStatus.Paid, EventDate, LongBeforeEvent);
        string s2 = OrderStateMachine.Transition(s1, OrderStatus.Issued, EventDate, LongBeforeEvent);
        string s3 = OrderStateMachine.Transition(s2, OrderStatus.EntryPassed, EventDate, LongBeforeEvent);

        Assert.Equal(OrderStatus.Paid, s1);
        Assert.Equal(OrderStatus.Issued, s2);
        Assert.Equal(OrderStatus.EntryPassed, s3);
    }

    [Fact]
    public void Test3_RefundAllowedWellBeforeEvent()
    {
        string result = OrderStateMachine.Transition(
            OrderStatus.Issued, OrderStatus.Refund, EventDate, LongBeforeEvent);
        Assert.Equal(OrderStatus.Refund, result);
    }

    /// <summary>
    /// Возврат НЕОПЛАЧЕННОЙ брони (< 24 ч до концерта) разрешён: это не финансовый
    /// возврат, иначе истёкшая бронь навсегда занимала бы места.
    /// </summary>
    [Fact]
    public void Test3_UnpaidBookingRefundAllowedEvenWithin24Hours()
    {
        var lessThan24hBefore = EventDate.AddHours(-12);
        string result = OrderStateMachine.Transition(
            OrderStatus.Booking, OrderStatus.Refund, EventDate, lessThan24hBefore);
        Assert.Equal(OrderStatus.Refund, result);
    }

    [Fact]
    public void Test3_CanTransitionReflectsFsm()
    {
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Booking, OrderStatus.Paid));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Paid, OrderStatus.Issued));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Issued, OrderStatus.EntryPassed));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Booking, OrderStatus.Issued));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.EntryPassed, OrderStatus.Refund));
    }

    /// <summary>
    /// Тест 4: InvalidOperationException при недопустимом переходе FSM,
    /// при возврате менее чем за 24 часа и при редактировании закрытого документа.
    /// </summary>
    [Fact]
    public void Test4_InvalidTransitionThrows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.Transition(OrderStatus.Booking, OrderStatus.Issued, EventDate, LongBeforeEvent));

        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.Transition(OrderStatus.Booking, OrderStatus.EntryPassed, EventDate, LongBeforeEvent));
    }

    [Fact]
    public void Test4_RefundLessThan24HoursThrows()
    {
        var lessThan24hBefore = EventDate.AddHours(-12);
        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.Transition(OrderStatus.Issued, OrderStatus.Refund, EventDate, lessThan24hBefore));
    }

    [Fact]
    public void Test4_EditingClosedDocumentThrows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.Transition(OrderStatus.EntryPassed, OrderStatus.Refund, EventDate, LongBeforeEvent));

        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.Transition(OrderStatus.Refund, OrderStatus.Paid, EventDate, LongBeforeEvent));

        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.EnsureEditable(OrderStatus.Refund));

        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.EnsureEditable(OrderStatus.EntryPassed));
    }
}
