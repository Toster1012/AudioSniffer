using AudioSniffer.Models;
using Microsoft.EntityFrameworkCore;

namespace AudioSniffer.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> database_options)
            : base(database_options)
        {
        }

        public DbSet<RequestHistory> RequestHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder model_builder)
        {
            base.OnModelCreating(model_builder);

            // Configure RequestHistory entity
            model_builder.Entity<RequestHistory>(entity =>
            {
                entity.HasKey(history_entity => history_entity.Id);
                entity.Property(history_entity => history_entity.FileName).IsRequired().HasMaxLength(255);
                entity.Property(history_entity => history_entity.IsGenerated).IsRequired();
                entity.Property(history_entity => history_entity.RequestDate).IsRequired();
            });
        }
    }
}