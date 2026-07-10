using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Agents.Tools;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class GenerationWriteGuardTests
{
    [Fact]
    public async Task OldTokenZombieWriterCannotWriteOrReleaseReplacementLease()
    {
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), root)
            .Options;
        var oldLease = NewLease("repo-1", "task-a");
        var newLease = NewLease("repo-1", "task-b");
        await using (var setup = new TestDbContext(options))
        {
            setup.RepositoryGenerationLocks.Add(ToEntity(oldLease));
            setup.DocFiles.Add(new DocFile { Id = "doc-1", BranchLanguageId = "lang-1", Content = "from-b" });
            setup.DocCatalogs.Add(new DocCatalog
            {
                Id = "catalog-1",
                BranchLanguageId = "lang-1",
                Title = "Existing",
                Path = "existing",
                DocFileId = "doc-1"
            });
            await setup.SaveChangesAsync();
            setup.RepositoryGenerationLocks.Remove(await setup.RepositoryGenerationLocks.SingleAsync());
            setup.RepositoryGenerationLocks.Add(ToEntity(newLease));
            await setup.SaveChangesAsync();
        }

        await using (var zombie = new TestDbContext(options))
        {
            var guard = CreateGuard();
            await Assert.ThrowsAsync<GenerationLeaseLostException>(
                () => new DocTool(
                        zombie,
                        "lang-1",
                        "existing",
                        generationWriteGuard: guard,
                        generationLease: oldLease)
                    .WriteAsync("from-zombie-a"));
        }

        await using (var zombieCatalog = new TestDbContext(options))
        {
            var storage = new CatalogStorage(zombieCatalog, "lang-1", CreateGuard(), oldLease);
            await Assert.ThrowsAsync<GenerationLeaseLostException>(
                () => storage.UpdateNodeAsync(
                    "existing",
                    """{"title":"Zombie","path":"existing","order":0,"children":[]}"""));
        }

        await using (var releaseContext = new TestDbContext(options))
        {
            await new RepositoryGenerationLockService(releaseContext)
                .ReleaseLeaseAsync(releaseContext, oldLease);
        }

        await using var verification = new TestDbContext(options);
        Assert.Equal("from-b", (await verification.DocFiles.SingleAsync()).Content);
        Assert.Equal("Existing", (await verification.DocCatalogs.SingleAsync()).Title);
        Assert.Equal(newLease.LockId, (await verification.RepositoryGenerationLocks.SingleAsync()).Id);
    }

    [Fact]
    public async Task SqliteFencedWriteAndRecoveryRaceHasSingleWinner()
    {
        if (OperatingSystem.IsLinux())
        {
            NativeLibrary.SetDllImportResolver(
                typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly,
                (libraryName, assembly, searchPath) => libraryName == "e_sqlite3"
                    ? NativeLibrary.Load("libsqlite3.so.0", assembly, searchPath)
                    : IntPtr.Zero);
        }
        var databasePath = Path.Combine(Path.GetTempPath(), $"opendeepwiki-fence-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5")
            .Options;
        var lease = NewLease("repo-sqlite", "task-a");
        await using (var setup = new TestDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.RepositoryGenerationLocks.Add(ToEntity(lease));
            setup.DocFiles.Add(new DocFile { Id = "doc-sqlite", BranchLanguageId = "lang", Content = "initial" });
            await setup.SaveChangesAsync();
        }

        await using (var writer = new TestDbContext(options))
        await using (var recovery = new TestDbContext(options))
        {
            (await writer.DocFiles.SingleAsync()).Content = "fenced-write";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeTask = Task.Run(async () =>
            {
                await gate.Task;
                await CreateGuard().SaveChangesAsync(writer, lease);
            });
            var recoveryTask = Task.Run(async () =>
            {
                await gate.Task;
                return await recovery.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "RepositoryGenerationLocks"
                    WHERE "Id" = {lease.LockId} AND "UpdatedAt" < {DateTime.UtcNow.AddMinutes(-1)}
                    """);
            });
            gate.SetResult();
            await Task.WhenAll(writeTask, recoveryTask);
            Assert.Equal(0, await recoveryTask);
        }

        await using (var expire = new TestDbContext(options))
        {
            await expire.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "RepositoryGenerationLocks" SET "UpdatedAt" = {DateTime.UtcNow.AddHours(-2)}
                """);
        }

        await using (var zombie = new TestDbContext(options))
        await using (var recovery = new TestDbContext(options))
        {
            (await zombie.DocFiles.SingleAsync()).Content = "zombie-write";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeTask = Task.Run(async () =>
            {
                await gate.Task;
                await Assert.ThrowsAsync<GenerationLeaseLostException>(
                    () => CreateGuard().SaveChangesAsync(zombie, lease));
            });
            var recoveryTask = Task.Run(async () =>
            {
                await gate.Task;
                return await recovery.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "RepositoryGenerationLocks"
                    WHERE "Id" = {lease.LockId} AND "UpdatedAt" < {DateTime.UtcNow.AddMinutes(-1)}
                    """);
            });
            gate.SetResult();
            await Task.WhenAll(writeTask, recoveryTask);
            Assert.Equal(1, await recoveryTask);
        }

        await using var verification = new TestDbContext(options);
        Assert.Equal("fenced-write", (await verification.DocFiles.SingleAsync()).Content);
        Assert.Empty(await verification.RepositoryGenerationLocks.ToListAsync());
    }

    private static GenerationWriteGuard CreateGuard() =>
        new(Options.Create(new IncrementalUpdateOptions { StaleTaskTimeoutMinutes = 1 }));

    private static GenerationLeaseHandle NewLease(string repositoryId, string ownerId) =>
        new(Guid.NewGuid().ToString(), repositoryId, RepositoryGenerationLockOwnerType.IncrementalTask, ownerId);

    private static RepositoryGenerationLock ToEntity(GenerationLeaseHandle lease) => new()
    {
        Id = lease.LockId,
        RepositoryId = lease.RepositoryId,
        OwnerType = lease.OwnerType,
        OwnerId = lease.OwnerId,
        Scope = RepositoryGenerationLockScope.Branch,
        AcquiredAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options);
}
