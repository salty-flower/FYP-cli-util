namespace DataCollection.Core.Models.Errors;

public static class BusinessError
{
    public static DomainError DiscoveryServiceUnavailable(string serviceName) =>
        DomainError
            .Create(
                "BUSINESS_DISCOVERY_SERVICE_UNAVAILABLE",
                $"Discovery service '{serviceName}' is not available"
            )
            .WithDetail("serviceName", serviceName);

    public static DomainError NoResultsFound(string searchCriteria) =>
        DomainError
            .Create("BUSINESS_NO_RESULTS_FOUND", $"No results found for search criteria")
            .WithDetail("searchCriteria", searchCriteria);

    public static DomainError ProcessingFailed(string operation, string reason) =>
        DomainError
            .Create("BUSINESS_PROCESSING_FAILED", $"Processing operation '{operation}' failed")
            .WithDetail("operation", operation)
            .WithDetail("reason", reason);

    public static DomainError InvalidConfiguration(string configurationKey, string reason) =>
        DomainError
            .Create(
                "BUSINESS_INVALID_CONFIGURATION",
                $"Invalid configuration for '{configurationKey}'"
            )
            .WithDetail("configurationKey", configurationKey)
            .WithDetail("reason", reason);

    public static DomainError UnsupportedOperation(string operation, string context) =>
        DomainError
            .Create(
                "BUSINESS_UNSUPPORTED_OPERATION",
                $"Operation '{operation}' is not supported in context '{context}'"
            )
            .WithDetail("operation", operation)
            .WithDetail("context", context);

    public static DomainError MaximumRetriesExceeded(string operation, int maxRetries) =>
        DomainError
            .Create(
                "BUSINESS_MAX_RETRIES_EXCEEDED",
                $"Operation '{operation}' exceeded maximum retries"
            )
            .WithDetail("operation", operation)
            .WithDetail("maxRetries", maxRetries);

    public static DomainError OperationCancelled(string operation) =>
        DomainError
            .Create("BUSINESS_OPERATION_CANCELLED", $"Operation '{operation}' was cancelled")
            .WithDetail("operation", operation);
}
