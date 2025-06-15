using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public abstract class IssueProcessingError : ValueObject
{
    public abstract string Code { get; }
    public abstract string Message { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}

public sealed class InvalidUrlError : IssueProcessingError
{
    public string Url { get; }

    public InvalidUrlError(string url)
    {
        Url = url;
    }

    public override string Code => "INVALID_URL";
    public override string Message => $"Invalid GitHub issue URL format: {Url}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Url;
    }
}

public sealed class MissingParametersError : IssueProcessingError
{
    public string MissingParameters { get; }

    public MissingParametersError(string missingParameters)
    {
        MissingParameters = missingParameters;
    }

    public override string Code => "MISSING_PARAMETERS";
    public override string Message => $"Missing required parameters: {MissingParameters}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return MissingParameters;
    }
}

public sealed class IssueProcessingFailedError : IssueProcessingError
{
    public string Owner { get; }
    public string Repo { get; }
    public long IssueNumber { get; }
    public string Reason { get; }

    public IssueProcessingFailedError(string owner, string repo, long issueNumber, string reason)
    {
        Owner = owner;
        Repo = repo;
        IssueNumber = issueNumber;
        Reason = reason;
    }

    public override string Code => "ISSUE_PROCESSING_FAILED";
    public override string Message =>
        $"Failed to process issue {Owner}/{Repo}#{IssueNumber}: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Owner;
        yield return Repo;
        yield return IssueNumber;
        yield return Reason;
    }
}

public sealed class BatchProcessingFailedError : IssueProcessingError
{
    public int IssueCount { get; }
    public string Reason { get; }

    public BatchProcessingFailedError(int issueCount, string reason)
    {
        IssueCount = issueCount;
        Reason = reason;
    }

    public override string Code => "BATCH_PROCESSING_FAILED";
    public override string Message => $"Failed to process batch of {IssueCount} issues: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return IssueCount;
        yield return Reason;
    }
}
