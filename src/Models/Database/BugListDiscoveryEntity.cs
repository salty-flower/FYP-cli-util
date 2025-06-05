using System.ComponentModel.DataAnnotations;

namespace DataCollection.Models.Database;

public class BugListDiscoveryEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string Conf { get; set; }

    [Required]
    public required int Year { get; set; }

    [Required]
    public required string AnalysisJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
