using DataCollection.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace DataCollection.Services;

public class DataCollectionDbContext : DbContext
{
    public DataCollectionDbContext(DbContextOptions<DataCollectionDbContext> options)
        : base(options) { }

    public DbSet<PaperEntity> Papers { get; set; }
    public DbSet<PdfDataEntity> PdfData { get; set; }
    public DbSet<IssueAnalysisEntity> IssueAnalyses { get; set; }
    public DbSet<BugListDiscoveryEntity> BugListDiscoveries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaperEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity
                .HasIndex(e => new
                {
                    e.Doi,
                    e.Conf,
                    e.Year,
                })
                .IsUnique();
            entity.HasIndex(e => new { e.Conf, e.Year });

            entity
                .HasOne(e => e.PdfData)
                .WithOne(e => e.Paper)
                .HasForeignKey<PdfDataEntity>(e => e.PaperId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PdfDataEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity
                .HasIndex(e => new
                {
                    e.FileName,
                    e.Conf,
                    e.Year,
                })
                .IsUnique();
            entity.HasIndex(e => new { e.Conf, e.Year });
        });

        modelBuilder.Entity<IssueAnalysisEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity
                .HasIndex(e => new
                {
                    e.Owner,
                    e.Repository,
                    e.IssueNumber,
                })
                .IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<BugListDiscoveryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Conf, e.Year }).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
