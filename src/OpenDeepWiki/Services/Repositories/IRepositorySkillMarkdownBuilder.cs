using System.IO.Compression;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

public interface IRepositorySkillMarkdownBuilder
{
    string BuildSkillMarkdown(
        Repository repository,
        RepositoryBranch branch,
        BranchLanguage language,
        IReadOnlyList<DocCatalog> catalogs,
        DateTime generatedAtUtc);

    Task RefreshSkillMarkdownAsync(
        IContext context,
        Repository repository,
        RepositoryBranch branch,
        BranchLanguage language,
        CancellationToken cancellationToken = default,
        GenerationLeaseHandle? lease = null);

    Task StageSkillMarkdownAsync(
        IIncrementalWikiDraft draft,
        Repository repository,
        RepositoryBranch branch,
        BranchLanguage language,
        CancellationToken cancellationToken = default);

    Task AddDocumentsToArchiveAsync(
        ZipArchive archive,
        IReadOnlyList<DocCatalog> catalogs,
        string rootPath,
        CancellationToken cancellationToken = default);
}
