using System.ComponentModel.DataAnnotations;

namespace DataCollection.Core.Models.Database;

public class PaperEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string Title { get; set; }

    [Required]
    public required string Authors { get; set; }

    [Required]
    public required string Abstract { get; set; }

    [Required]
    public required string Url { get; set; }

    [Required]
    public required string Doi { get; set; }

    [Required]
    public required string Conf { get; set; }

    [Required]
    public required int Year { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual PdfDataEntity? PdfData { get; set; }

    public string SanitizedDoi => Doi.Replace("/", "-");

    public string DownloadLink => $"/doi/pdf/{Doi}";

    public string GetPdfPath(string baseDir) =>
        Path.Combine(baseDir, "paper-PDFs", $"{SanitizedDoi}.pdf");
}
