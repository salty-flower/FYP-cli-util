using ConsoleAppFramework;
using DataCollection.Application.Models.Commands;
using DataCollection.Application.Services;
using DataCollection.Core.Models.Errors;
using DataCollection.Infrastructure.Options;
using DataCollection.Presentation.Cli.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataCollection.Presentation.Cli.Commands;

[RegisterCommands("procedure")]
[ConsoleAppFilter<PathsOptionsFilter>]
public class SimplifiedProcedureCommands
{
    private readonly IProcedureAnalysisService procedureService;
    private readonly ILogger<SimplifiedProcedureCommands> logger;
    private readonly ProcedureAnalysisOptions options;

    public SimplifiedProcedureCommands(
        IProcedureAnalysisService procedureService,
        ILogger<SimplifiedProcedureCommands> logger,
        IOptions<ProcedureAnalysisOptions> options
    )
    {
        this.procedureService = procedureService;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<int> FilterTechniqueAndBugs(
        string bugTablesPattern = null,
        string techniquesPattern = null,
        string tempBugTablesFile = null,
        string tempTechniquesFile = null,
        string outputFile = null,
        bool keepTempFiles = false,
        CancellationToken cancellationToken = default
    )
    {
        var command = new FilterTechniqueAndBugsCommand
        {
            BugTablesPattern = bugTablesPattern ?? options.DefaultBugTablesPattern,
            TechniquesPattern = techniquesPattern ?? options.DefaultTechniquesPattern,
            TempBugTablesFile = tempBugTablesFile ?? options.DefaultTempBugTablesFile,
            TempTechniquesFile = tempTechniquesFile ?? options.DefaultTempTechniquesFile,
            OutputFile = outputFile ?? options.DefaultOutputFile,
            KeepTempFiles = keepTempFiles,
        };

        var result = await procedureService.FilterTechniqueAndBugsAsync(command, cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation("Filter technique and bugs analysis completed successfully");
            return 0; // Success: Will show actual paper count when implementation is complete
        }

        LogProcedureError(result.Error, "technique and bugs filter analysis");
        return GetErrorCode(result.Error);
    }

    [ConsoleAppFilter<PythonEngineInitFilter>]
    [ConsoleAppFilter<NLTKDataFilter>]
    public async Task<int> AnalyzeBugTerminology(
        string bugPattern = null,
        string outputFile = null,
        bool adjectivesOnly = false,
        CancellationToken cancellationToken = default
    )
    {
        var command = new AnalyzeBugTerminologyCommand
        {
            BugPattern = bugPattern ?? options.DefaultBugPattern,
            OutputFile = outputFile ?? options.DefaultTerminologyFile,
            AdjectivesOnly = adjectivesOnly,
        };

        var result = await procedureService.AnalyzeBugTerminologyAsync(command, cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation("Bug terminology analysis completed successfully");
            return 0; // Success: Will show actual sentence count when implementation is complete
        }

        LogProcedureError(result.Error, "bug terminology analysis");
        return GetErrorCode(result.Error);
    }

    public async Task<int> MergeBugTerminologyAnalysis(
        string analysisPattern = "bug-terminology-analysis.json",
        string outputDirectory = "analysis-results",
        CancellationToken cancellationToken = default
    )
    {
        var command = new MergeBugTerminologyAnalysisCommand
        {
            AnalysisPattern = analysisPattern,
            OutputDirectory = outputDirectory ?? options.DefaultOutputDirectory,
        };

        var result = await procedureService.MergeBugTerminologyAnalysisAsync(
            command,
            cancellationToken
        );

        if (result.IsSuccess)
        {
            logger.LogInformation("Merge analysis completed successfully");
            return result.Value; // Return the actual file count
        }

        LogProcedureError(result.Error, "merge bug terminology analysis");
        return GetErrorCode(result.Error);
    }

    private void LogProcedureError(ProcedureError error, string operation)
    {
        logger.LogError(
            "Error during {Operation}: {ErrorType} - {Message}",
            operation,
            error.GetType().Name,
            error.Message
        );
    }

    private static int GetErrorCode(ProcedureError error) =>
        error switch
        {
            PatternValidationError => 2,
            FileOperationError => 3,
            DataProcessingError => 4,
            AnalysisProcessingError => 5,
            _ => 1,
        };
}
