using System;
using Microsoft.EntityFrameworkCore;

namespace MarginCoin.Model
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public virtual DbSet<Order> Order { get; set; }
        public virtual DbSet<OrderTemplate> OrderTemplate { get; set; }
        public virtual DbSet<Spot> Spot { get; set; }
        public virtual DbSet<Symbol> Symbol { get; set; }

        [Obsolete("Use RuntimeSettings instead. This will be removed in a future version.")]
        public virtual DbSet<Setting> Settings { get; set; }

        public virtual DbSet<RuntimeSetting> RuntimeSettings { get; set; }
        public virtual DbSet<CandleHistory> CandleHistory { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Create composite index for fast candle queries
            modelBuilder.Entity<CandleHistory>()
                .HasIndex(c => new { c.Symbol, c.Interval, c.OpenTime })
                .IsUnique();

            // Index for getting latest candles by symbol
            modelBuilder.Entity<CandleHistory>()
                .HasIndex(c => new { c.Symbol, c.Interval, c.IsClosed, c.OpenTime });
        }
    }
}
