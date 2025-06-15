using DataCollection.Application.Models.IssueTracker.Profiles;

namespace DataCollection.Application.Features.BugDiscovery.Caching;

public interface IUserProfileCache
{
    Task<UserProfile?> GetAsync(string userLogin, long repositoryId);
    Task SetAsync(string userLogin, long repositoryId, UserProfile userProfile);
}
