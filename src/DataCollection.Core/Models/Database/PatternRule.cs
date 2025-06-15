using System.ComponentModel.DataAnnotations;

namespace DataCollection.Core.Models.Database;

public class PatternRule
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = "";

    [Required]
    [MaxLength(1000)]
    public string Pattern { get; set; } = "";

    [MaxLength(200)]
    public string Description { get; set; } = "";

    public double BaseConfidence { get; set; } = 0.8;

    public int Priority { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public class KeywordRule
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string Keyword { get; set; } = "";

    [MaxLength(500)]
    public string Description { get; set; } = "";

    public double BaseConfidence { get; set; } = 0.7;

    public int Priority { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public class UrlTypeRule
{
    public int Id { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Pattern { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string Type { get; set; } = "";

    [MaxLength(200)]
    public string Description { get; set; } = "";

    public int Priority { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public class ConfigurationRule
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string Key { get; set; } = "";

    [Required]
    [MaxLength(2000)]
    public string Value { get; set; } = "";

    [Required]
    [MaxLength(50)]
    public string DataType { get; set; } = "string"; // "string", "number", "boolean", "array"

    [MaxLength(500)]
    public string Description { get; set; } = "";

    public int Priority { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
