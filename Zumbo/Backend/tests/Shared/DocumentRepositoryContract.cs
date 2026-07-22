using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.RepositoryContracts;

public abstract class DocumentRepositoryContract
{
    protected abstract IDocumentRepository<RepositoryContractDocument> CreateRepository();

    [Fact]
    public async Task CreateSelectAndList_UseDetachedSnapshots()
    {
        var repository = CreateRepository();
        var id = NewId("detached");
        var source = new RepositoryContractDocument
        {
            Id = id,
            Name = "persisted",
            Value = 1,
            Tags = ["initial"]
        };

        try
        {
            var created = await repository.CreateAsync(source);
            source.Name = "source-mutated";
            source.Tags.Add("source-mutated");
            created.Name = "result-mutated";
            created.Tags.Add("result-mutated");

            var selected = await repository.SelectAsync(document => document.Id == id);
            Assert.NotNull(selected);
            Assert.Equal("persisted", selected.Name);
            Assert.Equal(["initial"], selected.Tags);

            selected.Name = "selected-mutated";
            selected.Tags.Add("selected-mutated");
            var listed = Assert.Single(await repository.ListByFilterAsync(document => document.Id == id));
            listed.Name = "listed-mutated";
            listed.Tags.Add("listed-mutated");

            var persisted = await repository.SelectAsync(document => document.Id == id);
            Assert.NotNull(persisted);
            Assert.Equal("persisted", persisted.Name);
            Assert.Equal(["initial"], persisted.Tags);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id == id);
        }
    }

    [Fact]
    public async Task Create_GeneratesIdAndRejectsDuplicateWithoutOverwriting()
    {
        var repository = CreateRepository();
        var generated = await repository.CreateAsync(new RepositoryContractDocument { Name = "generated" });
        var duplicateId = NewId("duplicate");

        try
        {
            Assert.False(string.IsNullOrWhiteSpace(generated.Id));
            await repository.CreateAsync(new RepositoryContractDocument
            {
                Id = duplicateId,
                Name = "first"
            });

            await Assert.ThrowsAsync<DocumentConflictException>(() => repository.CreateAsync(
                new RepositoryContractDocument { Id = duplicateId, Name = "second" }));

            var persisted = await repository.SelectAsync(document => document.Id == duplicateId);
            Assert.NotNull(persisted);
            Assert.Equal("first", persisted.Name);
        }
        finally
        {
            await repository.DeleteByFilterAsync(
                document => document.Id == generated.Id || document.Id == duplicateId);
        }
    }

    [Fact]
    public async Task ListByFilter_AppliesFilterStableOrderingAndPaging()
    {
        var repository = CreateRepository();
        var prefix = NewId("list");
        var documents = new[]
        {
            new RepositoryContractDocument { Id = $"{prefix}-c", Name = "bravo", Value = 3 },
            new RepositoryContractDocument { Id = $"{prefix}-a", Name = "alpha", Value = 1 },
            new RepositoryContractDocument { Id = $"{prefix}-b", Name = "bravo", Value = 2 }
        };

        try
        {
            foreach (var document in documents)
            {
                await repository.CreateAsync(document);
            }

            var page = await repository.ListByFilterAsync(
                document => document.Id.StartsWith(prefix) && document.Value > 0,
                document => document.Name,
                page: 2,
                pageSize: 1);
            var defaultOrder = await repository.ListByFilterAsync(
                document => document.Id.StartsWith(prefix),
                pageSize: 10);

            Assert.Equal($"{prefix}-b", Assert.Single(page).Id);
            Assert.Equal(
                [$"{prefix}-a", $"{prefix}-b", $"{prefix}-c"],
                defaultOrder.Select(document => document.Id));
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        }
    }

    [Fact]
    public async Task OffsetPagination_ClampsPageSizeToDocumentedBounds()
    {
        var repository = CreateRepository();
        var prefix = NewId("bounded");

        try
        {
            for (var index = 0; index < 205; index++)
            {
                await repository.CreateAsync(new RepositoryContractDocument
                {
                    Id = $"{prefix}-{index:D3}",
                    Name = index.ToString(),
                    Value = index
                });
            }

            var maximum = await repository.ListByFilterAsync(
                document => document.Id.StartsWith(prefix),
                pageSize: int.MaxValue);
            var minimum = await repository.ListByFilterAsync(
                document => document.Id.StartsWith(prefix),
                pageSize: 0);

            Assert.Equal(200, maximum.Count);
            Assert.Single(minimum);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        }
    }

    [Fact]
    public async Task CountAndExistence_ApplyTheSameFilter()
    {
        var repository = CreateRepository();
        var prefix = NewId("count");

        try
        {
            await repository.CreateAsync(new RepositoryContractDocument
                { Id = $"{prefix}-a", Name = "included", Value = 1 });
            await repository.CreateAsync(new RepositoryContractDocument
                { Id = $"{prefix}-b", Name = "excluded", Value = 2 });

            var count = await repository.CountByFilterAsync(
                document => document.Id.StartsWith(prefix) && document.Value == 1);
            var exists = await repository.ExistsByFilterAsync(
                document => document.Id.StartsWith(prefix) && document.Name == "included");
            var missing = await repository.ExistsByFilterAsync(
                document => document.Id.StartsWith(prefix) && document.Name == "missing");

            Assert.Equal(1, count);
            Assert.True(exists);
            Assert.False(missing);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        }
    }

    [Fact]
    public async Task CursorPagination_IsStableFilteredAndHasNoDuplicates()
    {
        var repository = CreateRepository();
        var prefix = NewId("cursor");

        try
        {
            for (var index = 0; index < 5; index++)
            {
                await repository.CreateAsync(new RepositoryContractDocument
                {
                    Id = $"{prefix}-{index:D2}",
                    Name = "included",
                    Value = index
                });
            }

            await repository.CreateAsync(new RepositoryContractDocument
                { Id = NewId("outside"), Name = "excluded" });

            var first = await repository.ListByCursorAsync(
                document => document.Id.StartsWith(prefix),
                pageSize: 2);
            var second = await repository.ListByCursorAsync(
                document => document.Id.StartsWith(prefix),
                first.NextCursor,
                pageSize: 2);
            var third = await repository.ListByCursorAsync(
                document => document.Id.StartsWith(prefix),
                second.NextCursor,
                pageSize: 2);
            var ids = first.Items.Concat(second.Items).Concat(third.Items)
                .Select(document => document.Id)
                .ToList();

            Assert.Equal(5, ids.Count);
            Assert.Equal(5, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
            Assert.NotNull(first.NextCursor);
            Assert.NotNull(second.NextCursor);
            Assert.Null(third.NextCursor);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
            await repository.DeleteByFilterAsync(document => document.Name == "excluded");
        }
    }

    [Fact]
    public async Task ReplaceAndUpdate_DistinguishNotFoundMatchedAndModified()
    {
        var repository = CreateRepository();
        var id = NewId("mutation");

        try
        {
            await repository.CreateAsync(new RepositoryContractDocument
            {
                Id = id,
                Name = "before",
                Value = 1
            });

            var replaced = await repository.ReplaceByFilterAsync(
                document => document.Id == id,
                new RepositoryContractDocument { Id = id, Name = "replaced", Value = 2 });
            var unchangedReplacement = await repository.ReplaceByFilterAsync(
                document => document.Id == id,
                new RepositoryContractDocument { Id = id, Name = "replaced", Value = 2 });
            var updated = await repository.UpdateOneFieldByFilterAsync(
                document => document.Id == id,
                document => document.Name,
                "updated");
            var unchangedUpdate = await repository.UpdateOneFieldByFilterAsync(
                document => document.Id == id,
                document => document.Name,
                "updated");
            var missingReplace = await repository.ReplaceByFilterAsync(
                document => document.Id == "missing",
                new RepositoryContractDocument { Id = "missing", Name = "missing" });
            var missingUpdate = await repository.UpdateOneFieldByFilterAsync(
                document => document.Id == "missing",
                document => document.Name,
                "missing");

            Assert.Equal(new DocumentMutationResult(1, 1), replaced);
            Assert.Equal(new DocumentMutationResult(1, 0), unchangedReplacement);
            Assert.Equal(new DocumentMutationResult(1, 1), updated);
            Assert.Equal(new DocumentMutationResult(1, 0), unchangedUpdate);
            Assert.Equal(new DocumentMutationResult(0, 0), missingReplace);
            Assert.Equal(new DocumentMutationResult(0, 0), missingUpdate);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id == id);
        }
    }

    [Fact]
    public async Task Update_RejectsComputedOrUnknownFieldSelectors()
    {
        var repository = CreateRepository();
        var id = NewId("invalid-field");

        try
        {
            await repository.CreateAsync(new RepositoryContractDocument { Id = id, Value = 1 });

            await Assert.ThrowsAsync<DocumentQueryException>(() => repository.UpdateOneFieldByFilterAsync(
                document => document.Id == id,
                document => document.Value + 1,
                2));

            var persisted = await repository.SelectAsync(document => document.Id == id);
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted.Value);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id == id);
        }
    }

    [Fact]
    public async Task Delete_ReturnsTheActualDeletedCount()
    {
        var repository = CreateRepository();
        var prefix = NewId("delete");

        await repository.CreateAsync(new RepositoryContractDocument { Id = $"{prefix}-a" });
        await repository.CreateAsync(new RepositoryContractDocument { Id = $"{prefix}-b" });

        var deleted = await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        var deletedAgain = await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));

        Assert.Equal(2, deleted);
        Assert.Equal(0, deletedAgain);
    }

    [Fact]
    public async Task CallerTimeoutCancellation_IsHonoredByEveryOperation()
    {
        var repository = CreateRepository();
        var id = NewId("cancellation");
        await repository.CreateAsync(new RepositoryContractDocument { Id = id, Name = "persisted" });
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        var token = timeout.Token;

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.CreateAsync(
                new RepositoryContractDocument { Id = NewId("cancelled-create") }, token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.SelectAsync(
                document => document.Id == id, token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ListByFilterAsync(
                document => document.Id == id, cancellationToken: token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ListByCursorAsync(
                document => document.Id == id, cancellationToken: token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.CountByFilterAsync(
                document => document.Id == id, token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ExistsByFilterAsync(
                document => document.Id == id, token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ReplaceByFilterAsync(
                document => document.Id == id,
                new RepositoryContractDocument { Id = id },
                token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.UpdateOneFieldByFilterAsync(
                document => document.Id == id,
                document => document.Name,
                "cancelled",
                token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.DeleteByFilterAsync(
                document => document.Id == id, token));
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id == id);
        }
    }

    [Fact]
    public async Task CompareExchange_IncrementsVersionAndRejectsStaleWriter()
    {
        var repository = CreateRepository();
        var id = NewId("cas-stale");

        try
        {
            var created = await repository.CreateAsync(new RepositoryContractDocument
            {
                Id = id,
                Name = "original"
            });
            var firstWriter = await repository.SelectAsync(document => document.Id == id);
            var staleWriter = await repository.SelectAsync(document => document.Id == id);
            Assert.NotNull(firstWriter);
            Assert.NotNull(staleWriter);
            Assert.Equal(1, created.Version);

            firstWriter.Name = "first-writer";
            var changed = await repository.ReplaceByVersionAsync(
                document => document.Id == id,
                firstWriter,
                firstWriter.Version);
            staleWriter.Name = "stale-writer";
            var conflict = await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                repository.ReplaceByVersionAsync(
                    document => document.Id == id,
                    staleWriter,
                    staleWriter.Version));

            var persisted = await repository.SelectAsync(document => document.Id == id);
            Assert.Equal(new DocumentCompareExchangeResult(1, 1, 2), changed);
            Assert.Equal(1, conflict.ExpectedVersion);
            Assert.Equal(2, conflict.ActualVersion);
            Assert.NotNull(persisted);
            Assert.Equal("first-writer", persisted.Name);
            Assert.Equal(2, persisted.Version);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id == id);
        }
    }

    [Fact]
    public async Task CompareExchange_AllowsOnlyOneParallelWriter()
    {
        var repository = CreateRepository();
        var id = NewId("cas-parallel");

        try
        {
            await repository.CreateAsync(new RepositoryContractDocument { Id = id, Name = "original" });
            var writers = await Task.WhenAll(
                repository.SelectAsync(document => document.Id == id),
                repository.SelectAsync(document => document.Id == id));
            Assert.All(writers, Assert.NotNull);
            writers[0]!.Name = "writer-a";
            writers[1]!.Name = "writer-b";

            var outcomes = await Task.WhenAll(writers.Select(async writer =>
            {
                try
                {
                    await repository.ReplaceByVersionAsync(
                        document => document.Id == id,
                        writer!,
                        writer!.Version);
                    return "changed";
                }
                catch (DocumentConcurrencyException)
                {
                    return "conflict";
                }
            }));

            Assert.Equal(1, outcomes.Count(outcome => outcome == "changed"));
            Assert.Equal(1, outcomes.Count(outcome => outcome == "conflict"));
            var persisted = await repository.SelectAsync(document => document.Id == id);
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted.Version);
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id == id);
        }
    }

    private static string NewId(string scenario) =>
        $"data001-{scenario}-{Guid.NewGuid():N}";
}

public sealed class RepositoryContractDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public List<string> Tags { get; set; } = [];
    public long Version { get; set; }
}
