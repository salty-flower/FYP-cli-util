using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using DataCollection.Core.Models.Errors;

namespace DataCollection.Core.Models.ValueObjects;

public sealed partial class Doi : ValueObject
{
    private Doi(string value)
    {
        Value = value;
    }

    public string Value { get; }

    [GeneratedRegex(@"^10\.\d{4,}.*", RegexOptions.Compiled)]
    private static partial Regex DoiPattern();

    public static Result<Doi, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationError.EmptyParameter(nameof(value));

        var normalizedValue = value.Trim();

        if (!normalizedValue.StartsWith("10."))
            normalizedValue = "10." + normalizedValue.TrimStart('1', '0', '.');

        if (!DoiPattern().IsMatch(normalizedValue))
            return ValidationError.InvalidDoi(normalizedValue);

        return new Doi(normalizedValue);
    }

    public static implicit operator string(Doi doi) => doi.Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
