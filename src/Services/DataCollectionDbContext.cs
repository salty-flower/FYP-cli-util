using DataCollection.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace DataCollection.Services;

public class DataCollectionDbContext : DbContext
{
    public DataCollectionDbContext(DbContextOptions<DataCollectionDbContext> options)
        : base(options) { }

    public DbSet<PaperEntity> Papers { get; set; }
    public DbSet<PdfDataEntity> PdfData { get; set; }

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
    }
}
