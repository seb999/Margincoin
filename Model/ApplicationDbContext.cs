using Microsoft.EntityFrameworkCore;

namespace MarginCoin.Model
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public virtual DbSet<Order> Order { get; set; }
    }
}