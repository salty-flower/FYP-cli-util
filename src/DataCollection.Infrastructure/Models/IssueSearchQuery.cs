using DataCollection.Core.Models.IssueTracker;

namespace DataCollection.Infrastructure.Models;

public record IssueSearchQuery
{
    public string? Query { get; init; }
    public IssueStatus? Status { get; init; }
    public string? Author { get; init; }
    public string? Assignee { get; init; }
    public List<string> Labels { get; init; } = [];
    public DateTimeOffset? CreatedAfter { get; init; }
    public DateTimeOffset? CreatedBefore { get; init; }
    public DateTimeOffset? UpdatedAfter { get; init; }
    public DateTimeOffset? UpdatedBefore { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }
    public Dictionary<string, object> ProviderSpecificFilters { get; init; } = new();
}
