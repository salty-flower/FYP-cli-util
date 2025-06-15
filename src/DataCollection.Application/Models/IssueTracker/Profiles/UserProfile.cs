using System.Text;

namespace DataCollection.Application.Models.IssueTracker.Profiles;

public record UserProfile
{
    public required string Login { get; init; }
    public bool? IsCollaboratorOrMember { get; init; }
    public required bool IsContributor { get; init; }

    public required int TotalIssues { get; init; }
    public required int TotalPullRequests { get; init; }
    public required int TotalMergedPullRequests { get; init; }

    public bool? IsDeveloper { get; init; }

    public override string ToString()
    {
        var sb = new StringBuilder($"@{Login} [");

        if (IsCollaboratorOrMember == true)
            sb.Append("Collaborator/Member");
        else
        {
            if (TotalIssues > 0)
                sb.Append($"opened {TotalIssues} issues");
            if (TotalPullRequests > 0)
            {
                sb.Append($", {TotalPullRequests} PRs (");
                if (TotalMergedPullRequests > 0)
                    if (TotalMergedPullRequests == TotalPullRequests)
                        sb.Append("all");
                    else
                        sb.Append($"{TotalMergedPullRequests}");
                else
                    sb.Append("no");
                sb.Append(" merged)");
            }

            if (IsContributor)
                sb.Append(", is contributor");
        }
        sb.Append(']');
        return sb.ToString();
    }
}
