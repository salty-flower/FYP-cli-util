namespace DataCollection.Application.Models.Commands;

public sealed record FilterTechniqueAndBugsCommand(
    string BugTablesPattern = @"Table\d+:*[^\s]*bugs",
    string TechniquesPattern =
        @"(differential.{1}testing|metamorphic.{1}testing|property-based|fuzzing)",
    string TempBugTablesFile = "bug-tables.json",
    string TempTechniquesFile = "techniques.json",
    string OutputFile = "research-analysis.json",
    bool KeepTempFiles = false
);

public sealed record AnalyzeBugTerminologyCommand(
    string BugPattern = @"\b(?:bug|bugs)\b",
    string OutputFile = "bug-terminology-analysis.json",
    bool AdjectivesOnly = false
);

public sealed record MergeBugTerminologyAnalysisCommand(
    string AnalysisPattern = "bug-terminology-analysis.json",
    string OutputDirectory = "analysis-results"
);
