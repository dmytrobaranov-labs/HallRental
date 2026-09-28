using HallRental.API.Services;
using Xunit;

namespace HallRental.UnitTests;

public class PricingCalculatorTests
{
    private readonly PricingCalculator _calculator = new();

    [Theory]
    [InlineData(1000, 7, 900)]   // Ранкова година (07:00): знижка 10% -> 900
    [InlineData(1000, 10, 1000)] // Стандартна година (10:00): без змін -> 1000
    [InlineData(1000, 13, 1150)] // Пікова година (13:00): націнка 15% -> 1150
    [InlineData(1000, 20, 800)]  // Вечірня година (20:00): знижка 20% -> 800
    public void CalculateHourlyRate_ShouldReturnExpectedRate_BasedOnHour(decimal basePrice, int hour, decimal expectedRate)
    {
        var result = _calculator.CalculateHourlyRate(basePrice, hour);

        Assert.Equal(expectedRate, result);
    }
}