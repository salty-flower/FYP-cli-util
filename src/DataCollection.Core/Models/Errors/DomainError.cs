using CSharpFunctionalExtensions;

namespace DataCollection.Core.Models.Errors;

public sealed class DomainError : ValueObject
{
    public string Code { get; }
    public string Message { get; }
    public Dictionary<string, object>? Details { get; }

    private DomainError(string code, string message, Dictionary<string, object>? details = null)
    {
        Code = code;
        Message = message;
        Details = details;
    }

    public static DomainError Create(
        string code,
        string message,
        Dictionary<string, object>? details = null
    ) => new(code, message, details);

    public DomainError WithDetail(string key, object value)
    {
        var newDetails =
            Details?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, object>();
        newDetails[key] = value;
        return new DomainError(Code, Message, newDetails);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
        if (Details != null)
        {
            foreach (var detail in Details.OrderBy(kvp => kvp.Key))
            {
                yield return detail.Key;
                yield return detail.Value;
            }
        }
    }

    public override string ToString() => $"[{Code}] {Message}";
}
