using Microsoft.EntityFrameworkCore;
using AudioSniffer.Models;

namespace AudioSniffer.Data;

public sealed class ApplicationDbContext : DbContext
{
    public DbSet<RequestHistory> RequestHistories { get; set; }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RequestHistory>().HasKey(r => r.Id);

        modelBuilder.Entity<RequestHistory>()
            .Property(r => r.AudioFileId)
            .IsRequired();

        modelBuilder.Entity<RequestHistory>()
            .Property(r => r.OverallConfidence);

        modelBuilder.Entity<RequestHistory>()
            .Property(r => r.IsNeuralNetwork);

        modelBuilder.Entity<RequestHistory>()
            .Property(r => r.DetectionsJson)
            .IsRequired();

        modelBuilder.Entity<RequestHistory>()
            .Property(r => r.DurationSeconds);

        modelBuilder.Entity<RequestHistory>()
            .Property(r => r.SampleRate);

        modelBuilder.Entity<RequestHistory>()
            .Property(r => r.Format);
    }
}