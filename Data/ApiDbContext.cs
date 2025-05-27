using BondRun.Models;
using Microsoft.EntityFrameworkCore;

namespace BondRun.Data;

public class ApiDbContext(DbContextOptions<ApiDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games { get; set; }
    public DbSet<Bet> Bets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>()
            .HasMany(e => e.Bets)
            .WithOne(e => e.Game)
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}