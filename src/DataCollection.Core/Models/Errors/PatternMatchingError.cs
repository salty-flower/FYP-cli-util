using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public abstract class PatternMatchingError : ValueObject
{
    public abstract string Code { get; }
    public abstract string Message { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}

public sealed class RuleFetchingError : PatternMatchingError
{
    public string RuleType { get; }
    public string Reason { get; }

    public RuleFetchingError(string ruleType, string reason)
    {
        RuleType = ruleType;
        Reason = reason;
    }

    public override string Code => "RULE_FETCHING_FAILED";
    public override string Message => $"Failed to fetch rules for '{RuleType}': {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return base.GetEqualityComponents();
        yield return RuleType;
    }
}

public sealed class PatternMatchingAnalysisError : PatternMatchingError
{
    public string Reason { get; }

    public PatternMatchingAnalysisError(string reason)
    {
        Reason = reason;
    }

    public override string Code => "PATTERN_MATCHING_FAILED";
    public override string Message => $"Pattern matching analysis failed: {Reason}";

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return base.GetEqualityComponents();
        yield return Reason;
    }
}

public sealed class PatternMatchingConfigurationError : PatternMatchingError
{
    public string Parameter { get; }
    public string Reason { get; }

    public PatternMatchingConfigurationError(string parameter, string reason)
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
