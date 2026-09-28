using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HallRental.API.Data;

/// <summary>
/// Потрібна лише для `dotnet ef migrations add ...`: без рядка підключення застосунок
/// реєструє сховище в пам'яті, тож інструменту EF треба явно показати, як створити контекст.
/// До цього рядка підключення інструмент при генерації міграцій не підключається.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HallRentalDbContext>
{
    public HallRentalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HallRentalDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=HallRental_Design;Trusted_Connection=True;")
            .Options;
        return new HallRentalDbContext(options);
    }
}
