using CSharpFunctionalExtensions;
using DataCollection.Application.Models.Commands;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Application.Services;

public interface IPdfProcessingService
{
    Task<Result<PdfProcessingResult, PdfProcessingError>> ProcessPdfsAsync(
        ProcessPdfsCommand command,
        CancellationToken cancellationToken = default
    );
}
