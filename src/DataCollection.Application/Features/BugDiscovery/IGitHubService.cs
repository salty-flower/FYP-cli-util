namespace DataCollection.Application.Features.BugDiscovery;

using DataCollection.Application.Models.IssueTracker.Profiles;
using DataCollection.Core.Models.IssueTracker;
using DataCollection.Core.Models.IssueTracker.Responses;
using GitHub.Models;

public interface IGitHubService
{
    Task<UserProfile> GetUserProfileAsync(
        string login,
        FullRepository repository,
        CancellationToken cancellationToken = default
    );

    Task<UserProfile> GetUserProfileAsync(
        string userLogin,
        object sdkUser,
        FullRepository repository,
        CancellationToken cancellationToken = default
    );

    Task<IssueProfile?> BuildComprehensiveIssueProfileAsync(
        string owner,
        string repoName,
        long issueNumber,
        CancellationToken cancellationToken = default
    );

    DeterministicIssueAnalysis SynthesizeDeterministicIssueAnalysis(IssueProfile profile);
}
