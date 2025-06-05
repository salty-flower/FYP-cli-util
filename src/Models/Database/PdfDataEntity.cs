using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataCollection.Models.Database;

public class PdfDataEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string FileName { get; set; }

    [Required]
    public required string Texts { get; set; }

    [Required]
    public required string TextLines { get; set; }

    [Required]
    public required string Conf { get; set; }

    [Required]
    public required int Year { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(Paper))]
    public int? PaperId { get; set; }

    public virtual PaperEntity? Paper { get; set; }
}
