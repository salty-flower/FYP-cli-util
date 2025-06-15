using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public abstract class PdfProcessingError : ValueObject
{
    public abstract string Code { get; }
    public abstract string Message { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}

public sealed class PdfDirectoryNotFoundError : PdfProcessingError
{
    public string DirectoryPath { get; }

    public PdfDirectoryNotFoundError(string directoryPath)
    {
        DirectoryPath = directoryPath;
    }

    public override string Code => "PDF_DIRECTORY_NOT_FOUND";
    public override string Message => $"PDF directory not found: {DirectoryPath}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return DirectoryPath;
    }
}

public sealed class PdfProcessingFailedError : PdfProcessingError
{
    public string FileName { get; }
    public string Reason { get; }

    public PdfProcessingFailedError(string fileName, string reason)
    {
        FileName = fileName;
        Reason = reason;
    }

    public override string Code => "PDF_PROCESSING_FAILED";
    public override string Message => $"Failed to process PDF '{FileName}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return FileName;
        yield return Reason;
    }
}

public sealed class PythonInteropError : PdfProcessingError
{
    public string Operation { get; }
    public string Reason { get; }

    public PythonInteropError(string operation, string reason)
    {
        Operation = operation;
        Reason = reason;
    }

    public override string Code => "PYTHON_INTEROP_FAILED";
    public override string Message => $"Python interop operation '{Operation}' failed: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Operation;
        yield return Reason;
    }
}

public sealed class DatabaseOperationError : PdfProcessingError
{
    public string Operation { get; }
    public string Reason { get; }

    public DatabaseOperationError(string operation, string reason)
    {
        Operation = operation;
        Reason = reason;
    }

    public override string Code => "DATABASE_OPERATION_FAILED";
    public override string Message => $"Database operation '{Operation}' failed: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Operation;
        yield return Reason;
    }
}
