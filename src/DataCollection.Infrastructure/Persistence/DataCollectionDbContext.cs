using DataCollection.Core.Models.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataCollection.Infrastructure.Persistence;

public class DataCollectionDbContext : DbContext
{
    public DataCollectionDbContext(DbContextOptions<DataCollectionDbContext> options)
        : base(options) { }

    public DbSet<PaperEntity> Papers { get; set; }
    public DbSet<PdfDataEntity> PdfData { get; set; }
    public DbSet<IssueAnalysisEntity> IssueAnalyses { get; set; }
    public DbSet<BugListDiscoveryEntity> BugListDiscoveries { get; set; }
    public DbSet<PatternRule> PatternRules { get; set; }
    public DbSet<KeywordRule> KeywordRules { get; set; }
    public DbSet<UrlTypeRule> UrlTypeRules { get; set; }

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

        modelBuilder.Entity<PatternRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Category, e.IsActive });
            entity.HasIndex(e => e.Priority);
        });

        modelBuilder.Entity<KeywordRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Category, e.IsActive });
            entity.HasIndex(e => e.Priority);
        });

        modelBuilder.Entity<UrlTypeRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Type, e.IsActive });
            entity.HasIndex(e => e.Priority);
        });
    }
}

public class DbContextFactory : IDesignTimeDbContextFactory<DataCollectionDbContext>
{
    public DataCollectionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DataCollectionDbContext>();
        var dbPath = Path.Combine("../data", "data-collection.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new DataCollectionDbContext(optionsBuilder.Options);
    }
}
