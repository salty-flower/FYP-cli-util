using System.ComponentModel.DataAnnotations;
using DataCollection.Models.IssueTracker;

namespace DataCollection.Models.Database;

public class IssueAnalysisEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string Owner { get; set; }

    [Required]
    public required string Repository { get; set; }

    [Required]
    public required long IssueNumber { get; set; }

    [Required]
    public required IssueStatus Status { get; set; }

    [Required]
    public required string AnalysisJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
