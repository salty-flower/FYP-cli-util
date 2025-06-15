using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public abstract class IssueAnalysisError : ValueObject
{
    public abstract string Code { get; }
    public abstract string Message { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}

public sealed class LlmAnalysisError : IssueAnalysisError
{
    public string Model { get; }
    public string Reason { get; }

    public LlmAnalysisError(string model, string reason)
    {
        Model = model;
        Reason = reason;
    }

    public override string Code => "LLM_ANALYSIS_FAILED";
    public override string Message => $"LLM analysis failed for model '{Model}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return base.GetEqualityComponents();
        yield return Model;
    }
}

public sealed class IssueAnalysisConfigurationError : IssueAnalysisError
{
    public string Parameter { get; }
    public string Reason { get; }

    public IssueAnalysisConfigurationError(string parameter, string reason)
    {
        Parameter = parameter;
        Reason = reason;
    }

    public override string Code => "CONFIGURATION_ERROR";
    public override string Message => $"Configuration error for '{Parameter}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return base.GetEqualityComponents();
        yield return Parameter;
    }
}
