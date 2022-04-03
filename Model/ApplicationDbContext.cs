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
    }
}