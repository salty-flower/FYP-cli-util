using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public abstract class ProcedureError : ValueObject
{
    public abstract string Code { get; }
    public abstract string Message { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}

public sealed class PatternValidationError : ProcedureError
{
    public string Pattern { get; }
    public string Reason { get; }

    public PatternValidationError(string pattern, string reason)
    {
        Pattern = pattern;
        Reason = reason;
    }

    public override string Code => "PATTERN_VALIDATION_FAILED";
    public override string Message => $"Invalid pattern '{Pattern}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Pattern;
        yield return Reason;
    }
}

public sealed class DataProcessingError : ProcedureError
{
    public string Operation { get; }
    public string Reason { get; }

    public DataProcessingError(string operation, string reason)
    {
        Operation = operation;
        Reason = reason;
    }

    public override string Code => "DATA_PROCESSING_FAILED";
    public override string Message => $"Data processing operation '{Operation}' failed: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Operation;
        yield return Reason;
    }
}

public sealed class FileOperationError : ProcedureError
{
    public string FilePath { get; }
    public string Operation { get; }
    public string Reason { get; }

    public FileOperationError(string filePath, string operation, string reason)
    {
        FilePath = filePath;
        Operation = operation;
        Reason = reason;
    }

    public override string Code => "FILE_OPERATION_FAILED";
    public override string Message => $"File {Operation} failed for '{FilePath}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return FilePath;
        yield return Operation;
        yield return Reason;
    }
}

public sealed class AnalysisProcessingError : ProcedureError
{
    public string AnalysisType { get; }
    public string Reason { get; }

    public AnalysisProcessingError(string analysisType, string reason)
    {
        AnalysisType = analysisType;
        Reason = reason;
    }

    public override string Code => "ANALYSIS_PROCESSING_FAILED";
    public override string Message => $"Analysis processing for '{AnalysisType}' failed: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return AnalysisType;
        yield return Reason;
    }
}
