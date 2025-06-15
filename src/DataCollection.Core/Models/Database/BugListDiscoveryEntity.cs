using System.ComponentModel.DataAnnotations;

namespace DataCollection.Core.Models.Database;

public class BugListDiscoveryEntity
{
    [Key]
    public int Id { get; init; }

    [Required]
    public required string Conf { get; init; }

    [Required]
    public required int Year { get; init; }

    [Required]
    public required string AnalysisJson { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
