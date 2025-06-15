using DataCollection.Core.Models;

namespace DataCollection.Application.Models.Commands;

public sealed record ProcessPdfsCommand;

public sealed record PdfProcessingResult(
    int TotalFiles,
    int ProcessedFiles,
    int SkippedFiles,
    IReadOnlyList<PdfData> ProcessedData
);
