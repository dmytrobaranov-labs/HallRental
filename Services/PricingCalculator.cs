namespace HallRental.API.Services;

public class PricingCalculator
{
    public decimal CalculateHourlyRate(decimal basePrice, int hour)
    {
        // Пікові години: націнка 15%
        if (hour >= 12 && hour < 14) return basePrice * 1.15m;

        // Вечірні години: знижка 20%
        if (hour >= 18 && hour < 23) return basePrice * 0.80m;

        // Ранкові години: знижка 10%
        if (hour >= 6 && hour < 9) return basePrice * 0.90m;

        // Стандартні години
        return basePrice;
    }
}