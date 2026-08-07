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

public sealed class IncrementalWikiPublishConflictException(string entityType, string entityId)
    : InvalidOperationException(
        $"Incremental wiki publish conflict: {entityType} '{entityId}' changed after the draft snapshot was read.");

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
    private enum DraftDocumentMutationKind
    {
        Write,
        Append,
        Edit
    }

    private readonly IContext _readContext;
    private readonly long _maxBytes;
    private readonly Func<CancellationToken, Task> _earlyAbortCheck;
    private readonly Dictionary<string, LanguageDraft> _languages = [];
    private readonly Dictionary<string, (string Markdown, DateTime GeneratedAtUtc)> _skills = [];
    private long _sizeBytes;

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
        _sizeBytes = Encoding.UTF8.GetByteCount(SourceHeadCommitId);
        if (_sizeBytes > _maxBytes)
        {
            throw new IncrementalWikiDraftLimitExceededException(_maxBytes);
        }
    }

    public string SourceHeadCommitId { get; }

    public long SizeBytes => _sizeBytes;

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
        var projectedCatalogs = language.Catalogs.ToDictionary(item => item.Key, item => CloneCatalog(item.Value));
        var projectedChanges = new HashSet<string>(language.ChangedCatalogIds);
        var catalog = projectedCatalogs.Values.FirstOrDefault(item =>
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
        projectedChanges.Add(catalog.Id);

        if (updatedItem.Children.Count > 0)
        {
            foreach (var child in projectedCatalogs.Values.Where(item =>
                         !item.IsDeleted && item.ParentId == catalog.Id))
            {
                child.MarkAsDeleted();
                projectedChanges.Add(child.Id);
            }

            AddOrRestoreCatalogItems(
                projectedCatalogs,
                projectedChanges,
                branchLanguageId,
                updatedItem.Children,
                catalog.Id);
        }

        var currentSize = language.Catalogs.Values.Sum(CatalogSizeBytes);
        var projectedSize = projectedCatalogs.Values.Sum(CatalogSizeBytes);
        EnsureCanGrow(projectedSize - currentSize);
        language.Catalogs = projectedCatalogs;
        language.ChangedCatalogIds = projectedChanges;
        _sizeBytes += projectedSize - currentSize;
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
            DraftDocumentMutationKind.Write,
            content,
            sourceFiles);

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
            DraftDocumentMutationKind.Append,
            content,
            sourceFiles);

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
            DraftDocumentMutationKind.Edit,
            newContent,
            sourceFiles: null,
            oldContent);

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
        return document is { IsDeleted: false };
    }

    public void StageSkillMarkdown(string branchLanguageId, string markdown, DateTime generatedAtUtc)
    {
        var previousSize = _skills.TryGetValue(branchLanguageId, out var previous)
            ? SkillSizeBytes(previous.Markdown)
            : 0;
        var nextSize = SkillSizeBytes(markdown);
        EnsureCanGrow(nextSize - previousSize);
        _skills[branchLanguageId] = (markdown, generatedAtUtc);
        _sizeBytes += nextSize - previousSize;
    }

    public async Task ApplyAsync(IContext publishContext, CancellationToken cancellationToken = default)
    {
        foreach (var language in _languages.Values)
        {
            var newDocuments = new List<DocFile>();
            var newCatalogs = new List<DocCatalog>();
            var documentUpdates = new List<(DocFile Draft, DraftRowVersion Original)>();
            var catalogUpdates = new List<(DocCatalog Draft, DraftRowVersion Original)>();

            // Collect all mutations before writing. New catalogs may point at
            // newly-created documents, so those documents must be persisted
            // before any catalog FK is written.
            foreach (var documentId in language.ChangedDocumentIds)
            {
                var draftDocument = language.Documents[documentId];
                if (language.DocumentOriginalVersions.TryGetValue(documentId, out var originalVersion))
                {
                    documentUpdates.Add((draftDocument, originalVersion));
                }
                else
                {
                    if (await publishContext.DocFiles.AnyAsync(item => item.Id == documentId, cancellationToken))
                    {
                        throw new IncrementalWikiPublishConflictException(nameof(DocFile), documentId);
                    }

                    newDocuments.Add(CloneDocument(draftDocument));
                }
            }

            foreach (var catalogId in language.ChangedCatalogIds)
            {
                var draftCatalog = language.Catalogs[catalogId];
                if (language.CatalogOriginalVersions.TryGetValue(catalogId, out var originalVersion))
                {
                    catalogUpdates.Add((draftCatalog, originalVersion));
                }
                else
                {
                    if (await publishContext.DocCatalogs.AnyAsync(item => item.Id == catalogId, cancellationToken))
                    {
                        throw new IncrementalWikiPublishConflictException(nameof(DocCatalog), catalogId);
                    }

                    newCatalogs.Add(CloneCatalog(draftCatalog));
                }
            }

            // Persist new document rows first. This also makes a new document
            // available to a catalog CAS update in the same publish transaction.
            if (newDocuments.Count > 0)
            {
                publishContext.DocFiles.AddRange(newDocuments);
                await publishContext.SaveChangesAsync(cancellationToken);
            }

            // SQLite enforces the self-referencing ParentId FK immediately.
            // Insert roots/parents before their descendants deterministically.
            if (newCatalogs.Count > 0)
            {
                foreach (var batch in OrderCatalogInsertionBatches(newCatalogs))
                {
                    foreach (var catalog in batch)
                    {
                        publishContext.DocCatalogs.Add(catalog);
                    }

                    // Flush each depth batch so descendants never reference
                    // an unpersisted parent under SQLite's immediate FK checks.
                    await publishContext.SaveChangesAsync(cancellationToken);
                }
            }

            // Existing rows are updated only after all new FK targets exist.
            foreach (var (draftDocument, originalVersion) in documentUpdates)
            {
                if (!await UpdateDocumentWithCasAsync(
                        publishContext,
                        draftDocument,
                        originalVersion,
                        cancellationToken))
                {
                    throw new IncrementalWikiPublishConflictException(nameof(DocFile), draftDocument.Id);
                }
            }

            foreach (var (draftCatalog, originalVersion) in catalogUpdates)
            {
                if (!await UpdateCatalogWithCasAsync(
                        publishContext,
                        draftCatalog,
                        originalVersion,
                        cancellationToken))
                {
                    throw new IncrementalWikiPublishConflictException(nameof(DocCatalog), draftCatalog.Id);
                }
            }
        }

        foreach (var (languageId, skill) in _skills)
        {
            if (!_languages.TryGetValue(languageId, out var language) ||
                !await UpdateSkillWithCasAsync(
                    publishContext,
                    languageId,
                    skill,
                    language.LanguageOriginalVersion,
                    cancellationToken))
            {
                throw new IncrementalWikiPublishConflictException(nameof(BranchLanguage), languageId);
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<DocCatalog>> OrderCatalogInsertionBatches(
        IEnumerable<DocCatalog> catalogs)
    {
        var pending = catalogs.ToDictionary(catalog => catalog.Id);
        var batches = new List<IReadOnlyList<DocCatalog>>();

        while (pending.Count > 0)
        {
            var ready = pending.Values
                .Where(catalog => catalog.ParentId is null || !pending.ContainsKey(catalog.ParentId))
                .OrderBy(catalog => catalog.ParentId is null ? 0 : 1)
                .ThenBy(catalog => catalog.Order)
                .ThenBy(catalog => catalog.Path, StringComparer.Ordinal)
                .ThenBy(catalog => catalog.Id, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
            {
                throw new InvalidOperationException("Draft catalog tree contains a cycle among new nodes.");
            }

            batches.Add(ready);
            foreach (var catalog in ready)
            {
                pending.Remove(catalog.Id);
            }
        }

        return batches;
    }

    private static async Task<bool> UpdateCatalogWithCasAsync(
        IContext context,
        DocCatalog draft,
        DraftRowVersion original,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Draft publish requires an EF Core DbContext.");
        }

        if (IsInMemory(dbContext))
        {
            var live = await context.DocCatalogs.FirstOrDefaultAsync(item => item.Id == draft.Id, cancellationToken);
            if (live is null || !Matches(live, original))
            {
                return false;
            }

            CopyCatalog(draft, live);
            return true;
        }

        var updated = IsSqlite(dbContext)
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "DocCatalogs"
                SET "ParentId" = {draft.ParentId}, "Title" = {draft.Title}, "Path" = {draft.Path},
                    "Order" = {draft.Order}, "DocFileId" = {draft.DocFileId},
                    "UpdatedAt" = {draft.UpdatedAt}, "DeletedAt" = {draft.DeletedAt},
                    "IsDeleted" = {draft.IsDeleted}
                WHERE "Id" = {draft.Id}
                  AND (("Version" = {original.Version}) OR ("Version" IS NULL AND {original.Version} IS NULL))
                  AND (("UpdatedAt" = {original.UpdatedAt}) OR ("UpdatedAt" IS NULL AND {original.UpdatedAt} IS NULL))
                """, cancellationToken)
            : await context.DocCatalogs
                .Where(item => item.Id == draft.Id &&
                               item.Version == original.Version &&
                               item.UpdatedAt == original.UpdatedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ParentId, draft.ParentId)
                    .SetProperty(item => item.Title, draft.Title)
                    .SetProperty(item => item.Path, draft.Path)
                    .SetProperty(item => item.Order, draft.Order)
                    .SetProperty(item => item.DocFileId, draft.DocFileId)
                    .SetProperty(item => item.UpdatedAt, draft.UpdatedAt)
                    .SetProperty(item => item.DeletedAt, draft.DeletedAt)
                    .SetProperty(item => item.IsDeleted, draft.IsDeleted), cancellationToken);
        return updated == 1;
    }

    private static async Task<bool> UpdateDocumentWithCasAsync(
        IContext context,
        DocFile draft,
        DraftRowVersion original,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Draft publish requires an EF Core DbContext.");
        }

        if (IsInMemory(dbContext))
        {
            var live = await context.DocFiles.FirstOrDefaultAsync(item => item.Id == draft.Id, cancellationToken);
            if (live is null || !Matches(live, original))
            {
                return false;
            }

            CopyDocument(draft, live);
            return true;
        }

        var updated = IsSqlite(dbContext)
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "DocFiles"
                SET "Content" = {draft.Content}, "SourceFiles" = {draft.SourceFiles},
                    "UpdatedAt" = {draft.UpdatedAt}, "DeletedAt" = {draft.DeletedAt},
                    "IsDeleted" = {draft.IsDeleted}
                WHERE "Id" = {draft.Id}
                  AND (("Version" = {original.Version}) OR ("Version" IS NULL AND {original.Version} IS NULL))
                  AND (("UpdatedAt" = {original.UpdatedAt}) OR ("UpdatedAt" IS NULL AND {original.UpdatedAt} IS NULL))
                """, cancellationToken)
            : await context.DocFiles
                .Where(item => item.Id == draft.Id &&
                               item.Version == original.Version &&
                               item.UpdatedAt == original.UpdatedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Content, draft.Content)
                    .SetProperty(item => item.SourceFiles, draft.SourceFiles)
                    .SetProperty(item => item.UpdatedAt, draft.UpdatedAt)
                    .SetProperty(item => item.DeletedAt, draft.DeletedAt)
                    .SetProperty(item => item.IsDeleted, draft.IsDeleted), cancellationToken);
        return updated == 1;
    }

    private static async Task<bool> UpdateSkillWithCasAsync(
        IContext context,
        string languageId,
        (string Markdown, DateTime GeneratedAtUtc) skill,
        DraftRowVersion original,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Draft publish requires an EF Core DbContext.");
        }

        var updatedAt = DateTime.UtcNow;
        if (IsInMemory(dbContext))
        {
            var live = await context.BranchLanguages.FirstOrDefaultAsync(item => item.Id == languageId, cancellationToken);
            if (live is null || !Matches(live, original))
            {
                return false;
            }

            live.SkillMarkdown = skill.Markdown;
            live.SkillGeneratedAt = skill.GeneratedAtUtc;
            live.UpdatedAt = updatedAt;
            return true;
        }

        var updated = IsSqlite(dbContext)
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "BranchLanguages"
                SET "SkillMarkdown" = {skill.Markdown}, "SkillGeneratedAt" = {skill.GeneratedAtUtc},
                    "UpdatedAt" = {updatedAt}
                WHERE "Id" = {languageId} AND "IsDeleted" = 0
                  AND (("Version" = {original.Version}) OR ("Version" IS NULL AND {original.Version} IS NULL))
                  AND (("UpdatedAt" = {original.UpdatedAt}) OR ("UpdatedAt" IS NULL AND {original.UpdatedAt} IS NULL))
                """, cancellationToken)
            : await context.BranchLanguages
                .Where(item => item.Id == languageId && !item.IsDeleted &&
                               item.Version == original.Version &&
                               item.UpdatedAt == original.UpdatedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.SkillMarkdown, skill.Markdown)
                    .SetProperty(item => item.SkillGeneratedAt, skill.GeneratedAtUtc)
                    .SetProperty(item => item.UpdatedAt, updatedAt), cancellationToken);
        return updated == 1;
    }

    private static bool Matches<T>(AggregateRoot<T> live, DraftRowVersion original) =>
        live.UpdatedAt == original.UpdatedAt &&
        ((live.Version is null && original.Version is null) ||
         (live.Version is not null && original.Version is not null && live.Version.SequenceEqual(original.Version)));

    private static bool IsInMemory(DbContext context) =>
        context.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true;

    private static bool IsSqlite(DbContext context) =>
        context.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true;

    private async Task<DraftDocumentMutationResult> MutateDocumentAsync(
        string branchLanguageId,
        string path,
        CancellationToken cancellationToken,
        DraftDocumentMutationKind mutationKind,
        string content,
        string? sourceFiles,
        string? oldContent = null)
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
                var delta = -Utf8Size(catalog.DocFileId);
                EnsureCanGrow(delta);
                catalog.DocFileId = null;
                catalog.UpdateTimestamp();
                language.ChangedCatalogIds.Add(catalog.Id);
                _sizeBytes += delta;
            }

            return new DraftDocumentMutationResult(DraftDocumentMutationStatus.NavigationNode);
        }

        DocFile? document = null;
        DraftRowVersion? loadedOriginalVersion = null;
        var wasCached = false;
        if (catalog.DocFileId is not null)
        {
            wasCached = language.Documents.TryGetValue(catalog.DocFileId, out document);
            if (!wasCached)
            {
                var loaded = await LoadDocumentSnapshotAsync(catalog.DocFileId, cancellationToken);
                document = loaded.Document;
                loadedOriginalVersion = loaded.OriginalVersion;
            }
        }

        var created = false;
        if (document is null)
        {
            if (mutationKind == DraftDocumentMutationKind.Edit)
            {
                return new DraftDocumentMutationResult(DraftDocumentMutationStatus.DocumentNotFound);
            }

            var documentId = Guid.NewGuid().ToString();
            var newDocument = new DocFile
            {
                Id = documentId,
                BranchLanguageId = branchLanguageId,
                Content = content,
                SourceFiles = sourceFiles
            };
            var delta = DocumentSizeBytes(newDocument) + Utf8Size(documentId) - Utf8Size(catalog.DocFileId);
            EnsureCanGrow(delta);
            document = newDocument;
            language.Documents[document.Id] = document;
            catalog.DocFileId = document.Id;
            catalog.UpdateTimestamp();
            language.ChangedCatalogIds.Add(catalog.Id);
            _sizeBytes += delta;
            created = true;
        }
        else
        {
            long delta;
            var currentDocumentSize = wasCached ? DocumentSizeBytes(document) : 0;
            switch (mutationKind)
            {
                case DraftDocumentMutationKind.Write:
                    delta = wasCached
                        ? Utf8Size(content) - Utf8Size(document.Content) +
                          Utf8Size(sourceFiles) - Utf8Size(document.SourceFiles)
                        : Utf8Size(document.Id) + Utf8Size(content) + Utf8Size(sourceFiles) + 64L;
                    EnsureCanGrow(delta);
                    document.Content = content;
                    document.SourceFiles = sourceFiles;
                    break;
                case DraftDocumentMutationKind.Append:
                    delta = Utf8Size(content) +
                            (sourceFiles is null ? 0 : Utf8Size(sourceFiles) - Utf8Size(document.SourceFiles)) +
                            (wasCached ? 0 : DocumentSizeBytes(document));
                    EnsureCanGrow(delta);
                    document.Content = string.Concat(document.Content, content);
                    if (sourceFiles is not null)
                    {
                        document.SourceFiles = sourceFiles;
                    }
                    break;
                case DraftDocumentMutationKind.Edit:
                    if (oldContent is null || !document.Content.Contains(oldContent, StringComparison.Ordinal))
                    {
                        return new DraftDocumentMutationResult(DraftDocumentMutationStatus.OldContentNotFound);
                    }

                    var replacementCount = CountOccurrences(document.Content, oldContent);
                    delta = (long)replacementCount * (Utf8Size(content) - Utf8Size(oldContent)) +
                            (wasCached ? 0 : currentDocumentSize);
                    EnsureCanGrow(delta);
                    document.Content = document.Content.Replace(oldContent, content, StringComparison.Ordinal);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutationKind));
            }

            _sizeBytes += delta;
            if (!wasCached)
            {
                language.Documents.Add(document.Id, document);
                language.DocumentOriginalVersions.Add(
                    document.Id,
                    loadedOriginalVersion ?? throw new InvalidOperationException("Loaded document version is missing."));
            }
        }

        document.UpdateTimestamp();
        language.ChangedDocumentIds.Add(document.Id);
        return new DraftDocumentMutationResult(
            created ? DraftDocumentMutationStatus.Created : DraftDocumentMutationStatus.Updated,
            document.Content.Length);
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = content.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private async Task<LanguageDraft> GetLanguageAsync(string branchLanguageId, CancellationToken cancellationToken)
    {
        if (_languages.TryGetValue(branchLanguageId, out var existing))
        {
            return existing;
        }

        var liveLanguageVersion = await _readContext.BranchLanguages
            .AsNoTracking()
            .Where(item => item.Id == branchLanguageId && !item.IsDeleted)
            .Select(item => new DraftRowVersion(item.Version, item.UpdatedAt))
            .FirstAsync(cancellationToken);
        var catalogQuery = _readContext.DocCatalogs
            .AsNoTracking()
            .Where(item => item.BranchLanguageId == branchLanguageId);
        var storedCatalogSize = await GetStoredCatalogSizeAsync(branchLanguageId, cancellationToken);
        if (storedCatalogSize > _maxBytes - _sizeBytes)
        {
            throw new IncrementalWikiDraftLimitExceededException(_maxBytes);
        }

        var catalogs = await catalogQuery.ToListAsync(cancellationToken);
        var catalogClones = catalogs.ToDictionary(item => item.Id, CloneCatalog);
        var delta = catalogClones.Values.Sum(CatalogSizeBytes);
        EnsureCanGrow(delta);
        var language = new LanguageDraft(
            catalogClones,
            catalogs.ToDictionary(item => item.Id, item => DraftRowVersion.From(item)),
            liveLanguageVersion with { Version = liveLanguageVersion.Version?.ToArray() });
        _languages.Add(branchLanguageId, language);
        _sizeBytes += delta;
        return language;
    }

    private async Task<long> GetStoredCatalogSizeAsync(
        string branchLanguageId,
        CancellationToken cancellationToken)
    {
        if (_readContext is not DbContext dbContext || IsInMemory(dbContext))
        {
            var snapshots = await _readContext.DocCatalogs
                .AsNoTracking()
                .Where(item => item.BranchLanguageId == branchLanguageId)
                .Select(item => new { item.Id, item.ParentId, item.Title, item.Path, item.DocFileId })
                .ToListAsync(cancellationToken);
            return snapshots.Sum(item =>
                Utf8Size(item.Id) + Utf8Size(item.ParentId) + Utf8Size(item.Title) +
                Utf8Size(item.Path) + Utf8Size(item.DocFileId) + 64L);
        }

        if (IsSqlite(dbContext))
        {
            return await dbContext.Database.SqlQuery<long>($"""
                    SELECT COALESCE(SUM(
                        COALESCE(length(CAST("Id" AS BLOB)), 0) +
                        COALESCE(length(CAST("ParentId" AS BLOB)), 0) +
                        COALESCE(length(CAST("Title" AS BLOB)), 0) +
                        COALESCE(length(CAST("Path" AS BLOB)), 0) +
                        COALESCE(length(CAST("DocFileId" AS BLOB)), 0) + 64), 0) AS "Value"
                    FROM "DocCatalogs"
                    WHERE "BranchLanguageId" = {branchLanguageId}
                    """)
                .SingleAsync(cancellationToken);
        }

        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            return await dbContext.Database.SqlQuery<long>($"""
                    SELECT COALESCE(SUM(
                        COALESCE(octet_length("Id"), 0) +
                        COALESCE(octet_length("ParentId"), 0) +
                        COALESCE(octet_length("Title"), 0) +
                        COALESCE(octet_length("Path"), 0) +
                        COALESCE(octet_length("DocFileId"), 0) + 64), 0)::bigint AS "Value"
                    FROM "DocCatalogs"
                    WHERE "BranchLanguageId" = {branchLanguageId}
                    """)
                .SingleAsync(cancellationToken);
        }

        return await _readContext.DocCatalogs
            .AsNoTracking()
            .Where(item => item.BranchLanguageId == branchLanguageId)
            .SumAsync(item =>
                (long)item.Id.Length + item.Title.Length + item.Path.Length +
                (item.ParentId == null ? 0 : item.ParentId.Length) +
                (item.DocFileId == null ? 0 : item.DocFileId.Length) + 64L,
                cancellationToken);
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

        var loaded = await LoadDocumentSnapshotAsync(documentId, cancellationToken);
        if (loaded.Document is null)
        {
            return null;
        }

        var delta = DocumentSizeBytes(loaded.Document);
        EnsureCanGrow(delta);
        language.Documents.Add(documentId, loaded.Document);
        language.DocumentOriginalVersions.Add(
            documentId,
            loaded.OriginalVersion ?? throw new InvalidOperationException("Loaded document version is missing."));
        _sizeBytes += delta;
        return loaded.Document;
    }

    private async Task<(DocFile? Document, DraftRowVersion? OriginalVersion)> LoadDocumentSnapshotAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        var remainingBytes = _maxBytes - _sizeBytes;
        var storedSize = await GetStoredDocumentSizeAsync(documentId, cancellationToken);
        if (storedSize > remainingBytes)
        {
            throw new IncrementalWikiDraftLimitExceededException(_maxBytes);
        }

        var liveDocument = await _readContext.DocFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == documentId && !item.IsDeleted, cancellationToken);
        if (liveDocument is null)
        {
            return (null, null);
        }

        var clone = CloneDocument(liveDocument);
        var delta = DocumentSizeBytes(clone);
        EnsureCanGrow(delta);
        return (clone, DraftRowVersion.From(liveDocument));
    }

    private async Task<long> GetStoredDocumentSizeAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        if (_readContext is not DbContext dbContext || IsInMemory(dbContext))
        {
            var snapshot = await _readContext.DocFiles
                .AsNoTracking()
                .Where(item => item.Id == documentId && !item.IsDeleted)
                .Select(item => new { item.Id, item.Content, item.SourceFiles })
                .FirstOrDefaultAsync(cancellationToken);
            return snapshot is null
                ? 0
                : Utf8Size(snapshot.Id) + Utf8Size(snapshot.Content) + Utf8Size(snapshot.SourceFiles) + 64L;
        }

        if (IsSqlite(dbContext))
        {
            return await dbContext.Database.SqlQuery<long>($"""
                    SELECT COALESCE(length(CAST("Id" AS BLOB)), 0) +
                           COALESCE(length(CAST("Content" AS BLOB)), 0) +
                           COALESCE(length(CAST("SourceFiles" AS BLOB)), 0) + 64 AS "Value"
                    FROM "DocFiles"
                    WHERE "Id" = {documentId} AND "IsDeleted" = 0
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            return await dbContext.Database.SqlQuery<long>($"""
                    SELECT COALESCE(octet_length("Id"), 0) +
                           COALESCE(octet_length("Content"), 0) +
                           COALESCE(octet_length("SourceFiles"), 0) + 64 AS "Value"
                    FROM "DocFiles"
                    WHERE "Id" = {documentId} AND NOT "IsDeleted"
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var minimumCharacterCount = await _readContext.DocFiles
            .AsNoTracking()
            .Where(item => item.Id == documentId && !item.IsDeleted)
            .Select(item => (long)item.Id.Length + item.Content.Length +
                            (item.SourceFiles == null ? 0 : item.SourceFiles.Length) + 64L)
            .FirstOrDefaultAsync(cancellationToken);
        return minimumCharacterCount;
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
        Dictionary<string, DocCatalog> catalogs,
        HashSet<string> changedCatalogIds,
        string branchLanguageId,
        IEnumerable<CatalogItem> items,
        string? parentId)
    {
        foreach (var item in items)
        {
            var catalog = catalogs.Values.FirstOrDefault(existing =>
                string.Equals(existing.Path, item.Path, StringComparison.Ordinal));
            if (catalog is null)
            {
                catalog = new DocCatalog
                {
                    Id = Guid.NewGuid().ToString(),
                    BranchLanguageId = branchLanguageId,
                    Path = item.Path
                };
                catalogs.Add(catalog.Id, catalog);
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
            changedCatalogIds.Add(catalog.Id);

            AddOrRestoreCatalogItems(
                catalogs,
                changedCatalogIds,
                branchLanguageId,
                item.Children,
                catalog.Id);
        }
    }

    private void EnsureCanGrow(long deltaBytes)
    {
        if (deltaBytes > 0 && _sizeBytes > _maxBytes - deltaBytes)
        {
            throw new IncrementalWikiDraftLimitExceededException(_maxBytes);
        }
    }

    private static long CatalogSizeBytes(DocCatalog catalog) =>
        Utf8Size(catalog.Id) + Utf8Size(catalog.ParentId) + Utf8Size(catalog.Title) +
        Utf8Size(catalog.Path) + Utf8Size(catalog.DocFileId) + 64L;

    private static long DocumentSizeBytes(DocFile document) =>
        Utf8Size(document.Id) + Utf8Size(document.Content) + Utf8Size(document.SourceFiles) + 64L;

    private static long SkillSizeBytes(string markdown) => Utf8Size(markdown) + 32L;

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

    private sealed class LanguageDraft(
        Dictionary<string, DocCatalog> catalogs,
        Dictionary<string, DraftRowVersion> catalogOriginalVersions,
        DraftRowVersion languageOriginalVersion)
    {
        public Dictionary<string, DocCatalog> Catalogs { get; set; } = catalogs;
        public Dictionary<string, DraftRowVersion> CatalogOriginalVersions { get; } = catalogOriginalVersions;
        public Dictionary<string, DocFile> Documents { get; } = [];
        public Dictionary<string, DraftRowVersion> DocumentOriginalVersions { get; } = [];
        public DraftRowVersion LanguageOriginalVersion { get; } = languageOriginalVersion;
        public HashSet<string> ChangedCatalogIds { get; set; } = [];
        public HashSet<string> ChangedDocumentIds { get; } = [];
    }

    private sealed record DraftRowVersion(byte[]? Version, DateTime? UpdatedAt)
    {
        public static DraftRowVersion From<T>(AggregateRoot<T> entity) =>
            new(entity.Version?.ToArray(), entity.UpdatedAt);
    }
}
