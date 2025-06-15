using System.ComponentModel.DataAnnotations;

namespace DataCollection.Infrastructure.Options;

public class ProcedureAnalysisOptions
{
    public const string SectionName = "ProcedureAnalysis";

    // Default patterns
    public string DefaultBugTablesPattern { get; set; } = @"Table\d+:*[^\s]*bugs";
    public string DefaultTechniquesPattern { get; set; } =
        @"(differential.{1}testing|metamorphic.{1}testing|property-based|fuzzing)";
    public string DefaultBugPattern { get; set; } = @"\b(?:bug|bugs)\b";

    // Default file names
    public string DefaultTempBugTablesFile { get; set; } = "bug-tables.json";
    public string DefaultTempTechniquesFile { get; set; } = "techniques.json";
    public string DefaultOutputFile { get; set; } = "research-analysis.json";
    public string DefaultTerminologyFile { get; set; } = "bug-terminology-analysis.json";
    public string DefaultOutputDirectory { get; set; } = "analysis-results";

    // Processing options
    public bool DefaultKeepTempFiles { get; set; } = false;
    public bool DefaultAdjectivesOnly { get; set; } = false;

    // Validation constraints
    [Range(1, 10000)]
    public int MaxPatternLength { get; set; } = 1000;

    [Range(1, 100)]
    public int MaxFileNameLength { get; set; } = 50;
}
