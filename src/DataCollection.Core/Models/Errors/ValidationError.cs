namespace DataCollection.Core.Models.Errors;

public static class ValidationError
{
    public static DomainError InvalidDoi(string doi) =>
        DomainError
            .Create("VALIDATION_INVALID_DOI", $"Invalid DOI format: {doi}")
            .WithDetail("doi", doi);

    public static DomainError EmptyParameter(string parameterName) =>
        DomainError
            .Create("VALIDATION_EMPTY_PARAMETER", $"Parameter '{parameterName}' cannot be empty")
            .WithDetail("parameter", parameterName);

    public static DomainError FileNotFound(string filePath) =>
        DomainError
            .Create("VALIDATION_FILE_NOT_FOUND", $"File not found: {filePath}")
            .WithDetail("filePath", filePath);

    public static DomainError InvalidFileFormat(string filePath, string expectedFormat) =>
        DomainError
            .Create(
                "VALIDATION_INVALID_FILE_FORMAT",
                $"Invalid file format. Expected: {expectedFormat}"
            )
            .WithDetail("filePath", filePath)
            .WithDetail("expectedFormat", expectedFormat);

    public static DomainError ParameterOutOfRange(
        string parameterName,
        object value,
        object min,
        object max
    ) =>
        DomainError
            .Create(
                "VALIDATION_PARAMETER_OUT_OF_RANGE",
                $"Parameter '{parameterName}' value {value} is out of range [{min}, {max}]"
            )
            .WithDetail("parameter", parameterName)
            .WithDetail("value", value)
            .WithDetail("min", min)
            .WithDetail("max", max);
}
