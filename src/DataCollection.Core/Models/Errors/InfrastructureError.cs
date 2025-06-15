namespace DataCollection.Core.Models.Errors;

public static class InfrastructureError
{
    public static DomainError DatabaseConnectionFailed(string connectionString, string reason) =>
        DomainError
            .Create("INFRASTRUCTURE_DATABASE_CONNECTION_FAILED", "Database connection failed")
            .WithDetail("connectionString", connectionString)
            .WithDetail("reason", reason);

    public static DomainError FileOperationFailed(
        string operation,
        string filePath,
        string reason
    ) =>
        DomainError
            .Create("INFRASTRUCTURE_FILE_OPERATION_FAILED", $"File {operation} failed")
            .WithDetail("operation", operation)
            .WithDetail("filePath", filePath)
            .WithDetail("reason", reason);

    public static DomainError HttpRequestFailed(string url, int? statusCode, string reason) =>
        DomainError
            .Create("INFRASTRUCTURE_HTTP_REQUEST_FAILED", $"HTTP request to {url} failed")
            .WithDetail("url", url)
            .WithDetail("statusCode", statusCode)
            .WithDetail("reason", reason);

    public static DomainError ExternalServiceUnavailable(string serviceName, string reason) =>
        DomainError
            .Create(
                "INFRASTRUCTURE_EXTERNAL_SERVICE_UNAVAILABLE",
                $"External service '{serviceName}' is unavailable"
            )
            .WithDetail("serviceName", serviceName)
            .WithDetail("reason", reason);

    public static DomainError SerializationFailed(Type type, string reason) =>
        DomainError
            .Create(
                "INFRASTRUCTURE_SERIALIZATION_FAILED",
                $"Failed to serialize/deserialize {type.Name}"
            )
            .WithDetail("type", type.Name)
            .WithDetail("reason", reason);

    public static DomainError PythonInteropFailed(string operation, string reason) =>
        DomainError
            .Create(
                "INFRASTRUCTURE_PYTHON_INTEROP_FAILED",
                $"Python interop operation '{operation}' failed"
            )
            .WithDetail("operation", operation)
            .WithDetail("reason", reason);
}
