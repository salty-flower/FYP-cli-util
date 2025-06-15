using DataCollection.Core.Models.Database;

namespace DataCollection.Core.Interfaces;

public interface IRuleProvider
{
    Task<IReadOnlyList<PatternRule>> GetPatternsAsync(
        string category,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<KeywordRule>> GetKeywordsAsync(
        string category,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<UrlTypeRule>> GetUrlTypeRulesAsync(
        CancellationToken cancellationToken = default
    );
}
