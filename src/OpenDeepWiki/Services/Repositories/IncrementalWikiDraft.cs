using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Services.Repositories;

public sealed class IncrementalWikiDraftLimitExceededException(long maxBytes)
    : InvalidOperationException($"Incremental wiki draft exceeded its {maxBytes}-byte limit.");

public sealed class IncrementalBaselineConflictException(string branchId, string? expectedBaseline)
    : InvalidOperationException(
        $"Incremental baseline changed before publish. BranchId: {branchId}, Expected: {expectedBaseline ?? "<null>"}.");

public sealed class LocalGitSourceVersionChangedException(string? expectedHead, string? actualHead)
    : InvalidOperationException(
        $"Local Git HEAD changed before publish. Expected: {expectedHead ?? "<null>"}, Actual: {actualHead ?? "<null>"}.");

public enum DraftDocumentMutationStatus
{
    Updated,
    Created,
    CatalogNotFound,
    NavigationNode,
    DocumentNotFound,
    OldContentNotFound
}

public sealed record DraftDocumentMutationResult(
    DraftDocumentMutationStatus Status,
    int ContentLength = 0);

public interface IIncrementalWikiDraft
{
    string SourceHeadCommitId { get; }
    long SizeBytes { get; }

    Task<IReadOnlyList<DocCatalog>> GetCatalogsAsync(
        string branchLanguageId,
        bool includeDocuments,
        CancellationToken cancellationToken = default);

    Task UpdateCatalogNodeAsync(
        string branchLanguageId,
        string path,
        CatalogItem updatedItem,
        CancellationToken cancellationToken = default);

    Task<DraftDocumentMutationResult> WriteDocumentAsync(
        string branchLanguageId,
        string path,
        string content,
        string? sourceFiles,
        CancellationToken cancellationToken = default);

    Task<DraftDocumentMutationResult> AppendDocumentAsync(
        string branchLanguageId,
        string path,
        string content,
        string? sourceFiles,
        CancellationToken cancellationToken = default);

    Task<DraftDocumentMutationResult> EditDocumentAsync(
        string branchLanguageId,
        string path,
        string oldContent,
        string newContent,
        CancellationToken cancellationToken = default);

    Task<string?> ReadDocumentAsync(
        string branchLanguageId,
        string path,
        CancellationToken cancellationToken = default);

    Task<bool> DocumentExistsAsync(
        string branchLanguageId,
        string path,
        CancellationToken cancellationToken = default);

    void StageSkillMarkdown(string branchLanguageId, string markdown, DateTime generatedAtUtc);

    Task ApplyAsync(IContext publishContext, CancellationToken cancellationToken = default);
}

public sealed class IncrementalWikiDraft : IIncrementalWikiDraft
{
    private readonly IContext _readContext;
    private readonly long _maxBytes;
    private readonly Func<CancellationToken, Task> _earlyAbortCheck;
    private readonly Dictionary<string, LanguageDraft> _languages = [];
    private readonly Dictionary<string, (string Markdown, DateTime GeneratedAtUtc)> _skills = [];

    public IncrementalWikiDraft(
        IContext readContext,
        string sourceHeadCommitId,
        long maxBytes,
        Func<CancellationToken, Task> earlyAbortCheck)
    {
        _readContext = readContext;
        SourceHeadCommitId = sourceHeadCommitId;
        _maxBytes = Math.Max(1, maxBytes);
        _earlyAbortCheck = earlyAbortCheck;
    }

    public string SourceHeadCommitId { get; }

    public long SizeBytes => CalculateSizeBytes();

    public async Task<IReadOnlyList<DocCatalog>> GetCatalogsAsync(
        string branchLanguageId,
        bool includeDocuments,
        CancellationToken cancellationToken = default)
    {
        var language = await GetLanguageAsync(branchLanguageId, cancellationToken);
        if (includeDocuments)
        {
            foreach (var catalog in language.Catalogs.Values.Where(item => !item.IsDeleted && item.DocFileId != null))
            {
                catalog.DocFile = await GetDocumentAsync(language, catalog.DocFileId!, cancellationToken);
            }

            EnsureWithinLimit();
        }

        return language.Catalogs.Values
            .Where(item => !item.IsDeleted)
            .OrderBy(item => item.Order)
            .Select(CloneCatalogWithDocument)
            .ToList();
    }

    public async Task UpdateCatalogNodeAsync(
        string branchLanguageId,
        string path,
        CatalogItem updatedItem,
        CancellationToken cancellationToken = default)
    {
        await _earlyAbortCheck(cancellationToken);
        var language = await GetLanguageAsync(branchLanguageId, cancellationToken);
        var catalog = language.Catalogs.Values.FirstOrDefault(item =>
            !item.IsDeleted && string.Equals(item.Path, path, StringComparison.Ordinal));
        if (catalog is null)
        {
            throw new InvalidOperationException($"Catalog node with path '{path}' not found.");
        }

        catalog.Title = updatedItem.Title;
        catalog.Order = updatedItem.Order;
        if (updatedItem.Children.Count > 0)
        {
            catalog.DocFileId = null;
        }
        catalog.UpdateTimestamp();
        language.ChangedCatalogIds.Add(catalog.Id);

        if (updatedItem.Children.Count > 0)
        {
            foreach (var child in language.Catalogs.Values.Where(item =>
                         !item.IsDeleted && item.ParentId == catalog.Id))
            {
                child.MarkAsDeleted();
                language.ChangedCatalogIds.Add(child.Id);
            }

            AddOrRestoreCatalogItems(language, branchLanguageId, updatedItem.Children, catalog.Id);
        }

        EnsureWithinLimit();
    }

    public Task<DraftDocumentMutationResult> WriteDocumentAsync(
        string branchLanguageId,
        string path,
        string content,
        string? sourceFiles,
        CancellationToken cancellationToken = default) =>
        MutateDocumentAsync(
            branchLanguageId,
            path,
            cancellationToken,
            (document, _) =>
            {
                document.Content = content;
                document.SourceFiles = sourceFiles;
                return true;
            },
            createContent: content,
            createSourceFiles: sourceFiles);

    public Task<DraftDocumentMutationResult> AppendDocumentAsync(
        string branchLanguageId,
        string path,
        string content,
        string? sourceFiles,
        CancellationToken cancellationToken = default) =>
        MutateDocumentAsync(
            branchLanguageId,
            path,
            cancellationToken,
            (document, _) =>
            {
                document.Content = string.Concat(document.Content, content);
                if (sourceFiles is not null)
                {
                    document.SourceFiles = sourceFiles;
                }
                return true;
            },
            createContent: content,
            createSourceFiles: sourceFiles);

    public Task<DraftDocumentMutationResult> EditDocumentAsync(
        string branchLanguageId,
        string path,
        string oldContent,
        string newContent,
        CancellationToken cancellationToken = default) =>
        MutateDocumentAsync(
            branchLanguageId,
            path,
            cancellationToken,
            (document, _) =>
            {
                if (!document.Content.Contains(oldContent, StringComparison.Ordinal))
                {
                    return false;
                }

                document.Content = document.Content.Replace(oldContent, newContent, StringComparison.Ordinal);
                return true;
            },
            allowCreate: false);

    public async Task<string?> ReadDocumentAsync(
        string branchLanguageId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var language = await GetLanguageAsync(branchLanguageId, cancellationToken);
        var catalog = FindLeafCatalog(language, path);
        if (catalog?.DocFileId is null)
        {
            return null;
        }

        var document = await GetDocumentAsync(language, catalog.DocFileId, cancellationToken);
        EnsureWithinLimit();
        return document?.Content;
    }

    public async Task<bool> DocumentExistsAsync(
        string branchLanguageId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var language = await GetLanguageAsync(branchLanguageId, cancellationToken);
        var catalog = FindLeafCatalog(language, path);
        if (catalog?.DocFileId is null)
        {
            return false;
        }

        var document = await GetDocumentAsync(language, catalog.DocFileId, cancellationToken);
        EnsureWithinLimit();
        return document is { IsDeleted: false };
    }

    public void StageSkillMarkdown(string branchLanguageId, string markdown, DateTime generatedAtUtc)
    {
        _skills[branchLanguageId] = (markdown, generatedAtUtc);
        EnsureWithinLimit();
    }

    public async Task ApplyAsync(IContext publishContext, CancellationToken cancellationToken = default)
    {
        foreach (var language in _languages.Values)
        {
            foreach (var catalogId in language.ChangedCatalogIds)
            {
                var draftCatalog = language.Catalogs[catalogId];
                var liveCatalog = await publishContext.DocCatalogs
                    .FirstOrDefaultAsync(item => item.Id == catalogId, cancellationToken);
                if (liveCatalog is null)
                {
                    publishContext.DocCatalogs.Add(CloneCatalog(draftCatalog));
                }
                else
                {
                    CopyCatalog(draftCatalog, liveCatalog);
                }
            }

            foreach (var documentId in language.ChangedDocumentIds)
            {
                var draftDocument = language.Documents[documentId];
                var liveDocument = await publishContext.DocFiles
                    .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);
                if (liveDocument is null)
                {
                    publishContext.DocFiles.Add(CloneDocument(draftDocument));
                }
                else
                {
                    CopyDocument(draftDocument, liveDocument);
                }
            }
        }

        foreach (var (languageId, skill) in _skills)
        {
            var language = await publishContext.BranchLanguages
                .FirstAsync(item => item.Id == languageId && !item.IsDeleted, cancellationToken);
            language.SkillMarkdown = skill.Markdown;
            language.SkillGeneratedAt = skill.GeneratedAtUtc;
            language.UpdateTimestamp();
        }
    }

    private async Task<DraftDocumentMutationResult> MutateDocumentAsync(
        string branchLanguageId,
        string path,
        CancellationToken cancellationToken,
        Func<DocFile, bool, bool> mutate,
        bool allowCreate = true,
        string? createContent = null,
        string? createSourceFiles = null)
    {
        await _earlyAbortCheck(cancellationToken);
        var language = await GetLanguageAsync(branchLanguageId, cancellationToken);
        var catalog = language.Catalogs.Values.FirstOrDefault(item =>
            !item.IsDeleted && string.Equals(item.Path, path, StringComparison.Ordinal));
        if (catalog is null)
        {
            return new DraftDocumentMutationResult(DraftDocumentMutationStatus.CatalogNotFound);
        }

        if (language.Catalogs.Values.Any(item => !item.IsDeleted && item.ParentId == catalog.Id))
        {
            if (catalog.DocFileId is not null)
            {
                catalog.DocFileId = null;
                catalog.UpdateTimestamp();
                language.ChangedCatalogIds.Add(catalog.Id);
                EnsureWithinLimit();
            }

            return new DraftDocumentMutationResult(DraftDocumentMutationStatus.NavigationNode);
        }

        DocFile? document = null;
        if (catalog.DocFileId is not null)
        {
            document = await GetDocumentAsync(language, catalog.DocFileId, cancellationToken);
        }

        var created = false;
        if (document is null)
        {
            if (!allowCreate)
            {
                return new DraftDocumentMutationResult(DraftDocumentMutationStatus.DocumentNotFound);
            }

            document = new DocFile
            {
                Id = Guid.NewGuid().ToString(),
                BranchLanguageId = branchLanguageId,
                Content = createContent ?? string.Empty,
                SourceFiles = createSourceFiles
            };
            language.Documents[document.Id] = document;
            catalog.DocFileId = document.Id;
            catalog.UpdateTimestamp();
            language.ChangedCatalogIds.Add(catalog.Id);
            created = true;
        }
        else if (!mutate(document, false))
        {
            return new DraftDocumentMutationResult(DraftDocumentMutationStatus.OldContentNotFound);
        }

        document.UpdateTimestamp();
        language.ChangedDocumentIds.Add(document.Id);
        EnsureWithinLimit();
        return new DraftDocumentMutationResult(
            created ? DraftDocumentMutationStatus.Created : DraftDocumentMutationStatus.Updated,
            document.Content.Length);
    }

    private async Task<LanguageDraft> GetLanguageAsync(string branchLanguageId, CancellationToken cancellationToken)
    {
        if (_languages.TryGetValue(branchLanguageId, out var existing))
        {
            return existing;
        }

        var catalogs = await _readContext.DocCatalogs
            .AsNoTracking()
            .Where(item => item.BranchLanguageId == branchLanguageId)
            .ToListAsync(cancellationToken);
        var language = new LanguageDraft(catalogs.ToDictionary(item => item.Id, CloneCatalog));
        _languages.Add(branchLanguageId, language);
        EnsureWithinLimit();
        return language;
    }

    private async Task<DocFile?> GetDocumentAsync(
        LanguageDraft language,
        string documentId,
        CancellationToken cancellationToken)
    {
        if (language.Documents.TryGetValue(documentId, out var cached))
        {
            return cached.IsDeleted ? null : cached;
        }

        var liveDocument = await _readContext.DocFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == documentId && !item.IsDeleted, cancellationToken);
        if (liveDocument is null)
        {
            return null;
        }

        var clone = CloneDocument(liveDocument);
        language.Documents.Add(documentId, clone);
        return clone;
    }

    private static DocCatalog? FindLeafCatalog(LanguageDraft language, string path)
    {
        var catalog = language.Catalogs.Values.FirstOrDefault(item =>
            !item.IsDeleted && string.Equals(item.Path, path, StringComparison.Ordinal));
        return catalog is not null && language.Catalogs.Values.All(item => item.IsDeleted || item.ParentId != catalog.Id)
            ? catalog
            : null;
    }

    private static void AddOrRestoreCatalogItems(
        LanguageDraft language,
        string branchLanguageId,
        IEnumerable<CatalogItem> items,
        string? parentId)
    {
        foreach (var item in items)
        {
            var catalog = language.Catalogs.Values.FirstOrDefault(existing =>
                string.Equals(existing.Path, item.Path, StringComparison.Ordinal));
            if (catalog is null)
            {
                catalog = new DocCatalog
                {
                    Id = Guid.NewGuid().ToString(),
                    BranchLanguageId = branchLanguageId,
                    Path = item.Path
                };
                language.Catalogs.Add(catalog.Id, catalog);
            }

            catalog.ParentId = parentId;
            catalog.Title = item.Title;
            catalog.Order = item.Order;
            catalog.IsDeleted = false;
            catalog.DeletedAt = null;
            if (item.Children.Count > 0)
            {
                catalog.DocFileId = null;
            }
            catalog.UpdateTimestamp();
            language.ChangedCatalogIds.Add(catalog.Id);

            AddOrRestoreCatalogItems(language, branchLanguageId, item.Children, catalog.Id);
        }
    }

    private long CalculateSizeBytes()
    {
        long size = Encoding.UTF8.GetByteCount(SourceHeadCommitId);
        foreach (var language in _languages.Values)
        {
            foreach (var catalog in language.Catalogs.Values)
            {
                size += Utf8Size(catalog.Id) + Utf8Size(catalog.ParentId) + Utf8Size(catalog.Title) +
                        Utf8Size(catalog.Path) + Utf8Size(catalog.DocFileId) + 64;
            }

            foreach (var document in language.Documents.Values)
            {
                size += Utf8Size(document.Id) + Utf8Size(document.Content) + Utf8Size(document.SourceFiles) + 64;
            }
        }

        foreach (var skill in _skills.Values)
        {
            size += Utf8Size(skill.Markdown) + 32;
        }

        return size;
    }

    private void EnsureWithinLimit()
    {
        if (CalculateSizeBytes() > _maxBytes)
        {
            throw new IncrementalWikiDraftLimitExceededException(_maxBytes);
        }
    }

    private static int Utf8Size(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static DocCatalog CloneCatalogWithDocument(DocCatalog source)
    {
        var clone = CloneCatalog(source);
        clone.DocFile = source.DocFile is null ? null : CloneDocument(source.DocFile);
        return clone;
    }

    private static DocCatalog CloneCatalog(DocCatalog source) => new()
    {
        Id = source.Id,
        BranchLanguageId = source.BranchLanguageId,
        ParentId = source.ParentId,
        Title = source.Title,
        Path = source.Path,
        Order = source.Order,
        DocFileId = source.DocFileId,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        DeletedAt = source.DeletedAt,
        IsDeleted = source.IsDeleted,
        Version = source.Version?.ToArray()
    };

    private static DocFile CloneDocument(DocFile source) => new()
    {
        Id = source.Id,
        BranchLanguageId = source.BranchLanguageId,
        Content = source.Content,
        SourceFiles = source.SourceFiles,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        DeletedAt = source.DeletedAt,
        IsDeleted = source.IsDeleted,
        Version = source.Version?.ToArray()
    };

    private static void CopyCatalog(DocCatalog source, DocCatalog target)
    {
        target.ParentId = source.ParentId;
        target.Title = source.Title;
        target.Path = source.Path;
        target.Order = source.Order;
        target.DocFileId = source.DocFileId;
        target.UpdatedAt = source.UpdatedAt;
        target.DeletedAt = source.DeletedAt;
        target.IsDeleted = source.IsDeleted;
    }

    private static void CopyDocument(DocFile source, DocFile target)
    {
        target.Content = source.Content;
        target.SourceFiles = source.SourceFiles;
        target.UpdatedAt = source.UpdatedAt;
        target.DeletedAt = source.DeletedAt;
        target.IsDeleted = source.IsDeleted;
    }

    private sealed class LanguageDraft(Dictionary<string, DocCatalog> catalogs)
    {
        public Dictionary<string, DocCatalog> Catalogs { get; } = catalogs;
        public Dictionary<string, DocFile> Documents { get; } = [];
        public HashSet<string> ChangedCatalogIds { get; } = [];
        public HashSet<string> ChangedDocumentIds { get; } = [];
    }
}
