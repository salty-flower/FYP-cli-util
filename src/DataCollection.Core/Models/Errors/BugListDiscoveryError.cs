using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public abstract class BugListDiscoveryError : ValueObject
{
    public abstract string Code { get; }
    public abstract string Message { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}

public sealed class DoiValidationError : BugListDiscoveryError
{
    public string InvalidDoi { get; }

    public DoiValidationError(string invalidDoi)
    {
        InvalidDoi = invalidDoi;
    }

    public override string Code => "DOI_VALIDATION_FAILED";
    public override string Message => $"Invalid DOI format: {InvalidDoi}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return InvalidDoi;
    }
}

public sealed class FileNotFoundError : BugListDiscoveryError
{
    public string FilePath { get; }

    public FileNotFoundError(string filePath)
    {
        FilePath = filePath;
    }

    public override string Code => "FILE_NOT_FOUND";
    public override string Message => $"File not found: {FilePath}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return FilePath;
    }
}

public sealed class DiscoveryServiceUnavailableError : BugListDiscoveryError
{
    public string ServiceName { get; }

    public DiscoveryServiceUnavailableError(string serviceName)
    {
        ServiceName = serviceName;
    }

    public override string Code => "DISCOVERY_SERVICE_UNAVAILABLE";
    public override string Message => $"Discovery service '{ServiceName}' is not available";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return ServiceName;
    }
}

public sealed class ProcessingFailedError : BugListDiscoveryError
{
    public string Operation { get; }
    public string Reason { get; }

    public ProcessingFailedError(string operation, string reason)
    {
        Operation = operation;
        Reason = reason;
    }

    public override string Code => "PROCESSING_FAILED";
    public override string Message => $"Processing operation '{Operation}' failed: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Operation;
        yield return Reason;
    }
}

public sealed class StorageFailedError : BugListDiscoveryError
{
    public string Reason { get; }

    public StorageFailedError(string reason)
    {
        Reason = reason;
    }

    public override string Code => "STORAGE_FAILED";
    public override string Message => $"Failed to save to storage: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Reason;
    }
}
