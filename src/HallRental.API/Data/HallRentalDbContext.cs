using HallRental.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HallRental.API.Data;

public class HallRentalDbContext(DbContextOptions<HallRentalDbContext> options) : DbContext(options)
{
    public DbSet<HallDto> Halls => Set<HallDto>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HallDto>(e =>
        {
            e.ToTable("Halls");
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedNever();   // Id генерує HallService
            e.Property(h => h.Name).HasMaxLength(200).IsRequired();
            e.Property(h => h.BasePricePerHour).HasPrecision(18, 2);

            // Список послуг зберігається як JSON-колонка в таблиці Halls
            e.OwnsMany(h => h.Accessories, a => a.ToJson());
        });

        modelBuilder.Entity<Booking>(e =>
        {
            e.ToTable("Bookings");
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).ValueGeneratedNever();
            e.Property(b => b.TotalCost).HasPrecision(18, 2);

            // Прискорює перевірку колізій та пошук вільних залів
            e.HasIndex(b => new { b.HallId, b.StartTime, b.EndTime });
        });
    }
}
