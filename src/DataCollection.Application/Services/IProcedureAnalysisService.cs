using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Application.Models.Export.BugAnalysis;
using DataCollection.Application.Models.Export.Results;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Application.Services;

public interface IProcedureAnalysisService
{
    Task<Result<AnalysisOutput, ProcedureError>> FilterTechniqueAndBugsAsync(
        FilterTechniqueAndBugsCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<BugTerminologyAnalysis, ProcedureError>> AnalyzeBugTerminologyAsync(
        AnalyzeBugTerminologyCommand command,
        CancellationToken cancellationToken = default
    );

    Task<Result<int, ProcedureError>> MergeBugTerminologyAnalysisAsync(
        MergeBugTerminologyAnalysisCommand command,
        CancellationToken cancellationToken = default
    );
}
