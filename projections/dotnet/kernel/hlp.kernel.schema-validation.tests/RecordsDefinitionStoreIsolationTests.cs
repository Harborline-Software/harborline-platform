using System.Runtime.CompilerServices;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsDefinitionStoreIsolationTests
{
    [Fact]
    public async Task Returned_draft_does_not_expose_stored_nested_collections()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        var definition = Definition("1.0.0");

        await store.CreateDraftAsync(definition, expectedRevision: 0);
        var returned = await store.CreateDraftAsync(definition, expectedRevision: 0);
        MutateNestedCollections(returned.Definition);

        var stored = await store.CreateDraftAsync(definition, expectedRevision: 0);

        AssertStoredCollectionsAreUnchanged(stored.Definition);
    }

    [Fact]
    public async Task Returned_publication_does_not_expose_stored_nested_collections()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        var draft = await store.CreateDraftAsync(Definition("1.0.0"), expectedRevision: 0);

        await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);
        var returned = await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);
        MutateNestedCollections(returned.Definition);

        var stored = await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);

        AssertStoredCollectionsAreUnchanged(stored.Definition);
    }

    [Fact]
    public async Task Returned_published_head_does_not_expose_stored_nested_collections()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        var draft = await store.CreateDraftAsync(Definition("1.0.0"), expectedRevision: 0);
        await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);

        var returned = await store.GetPublishedHeadAsync("tenant-a", "definition.asset");
        Assert.NotNull(returned);
        MutateNestedCollections(returned.Definition);

        var stored = await store.GetPublishedHeadAsync("tenant-a", "definition.asset");
        Assert.NotNull(stored);
        AssertStoredCollectionsAreUnchanged(stored.Definition);
    }

    [Fact]
    public async Task Returned_history_does_not_expose_stored_nested_collections()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        var draft = await store.CreateDraftAsync(Definition("1.0.0"), expectedRevision: 0);
        await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);

        var returned = await store.ListHistoryAsync("tenant-a", "definition.asset");
        MutateNestedCollections(returned[0].Definition);
        MutateNestedCollections(returned[1].Definition);

        var stored = await store.ListHistoryAsync("tenant-a", "definition.asset");
        Assert.All(stored, revision => AssertStoredCollectionsAreUnchanged(revision.Definition));
    }

    [Fact]
    public async Task Draft_replay_requires_the_original_expected_revision_and_payload()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        var original = Definition("1.0.0");
        var first = await store.CreateDraftAsync(original, expectedRevision: 0);
        var second = await store.CreateDraftAsync(Definition("2.0.0"), first.Revision);

        var replay = await store.CreateDraftAsync(original, expectedRevision: 0);
        var wrongFence = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(() =>
            store.CreateDraftAsync(original, expectedRevision: first.Revision).AsTask());
        var wrongPayload = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(() =>
            store.CreateDraftAsync(
                original with { Name = "Changed" },
                expectedRevision: 0).AsTask());

        Assert.Equal(first.Revision, replay.Revision);
        Assert.Equal(
            RecordsDefinitionJson.SerializeCanonical(first.Definition),
            RecordsDefinitionJson.SerializeCanonical(replay.Definition));
        Assert.Equal("records.definition.expected_revision_conflict", wrongFence.Code);
        Assert.Equal(second.Revision, wrongFence.CurrentRevision);
        Assert.Equal("records.definition.expected_revision_conflict", wrongPayload.Code);
        Assert.Equal(second.Revision, wrongPayload.CurrentRevision);
        Assert.Equal(2, (await store.ListHistoryAsync("tenant-a", "definition.asset")).Count);
    }

    [Fact]
    public async Task Publish_replay_requires_the_original_expected_revision_and_payload()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        var draft = await store.CreateDraftAsync(Definition("1.0.0"), expectedRevision: 0);
        var published = await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);
        var laterDraft = await store.CreateDraftAsync(Definition("2.0.0"), published.Revision);

        var replay = await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);
        var wrongFence = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(() =>
            store.PublishAsync(
                "tenant-a",
                "definition.asset",
                "1.0.0",
                expectedRevision: published.Revision).AsTask());
        var wrongPayload = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(() =>
            store.PublishAsync(
                "tenant-a",
                "definition.asset",
                "2.0.0",
                expectedRevision: draft.Revision).AsTask());

        Assert.Equal(published.Revision, replay.Revision);
        Assert.Equal("records.definition.expected_revision_conflict", wrongFence.Code);
        Assert.Equal(laterDraft.Revision, wrongFence.CurrentRevision);
        Assert.Equal("records.definition.expected_revision_conflict", wrongPayload.Code);
        Assert.Equal(laterDraft.Revision, wrongPayload.CurrentRevision);
        Assert.Equal(3, (await store.ListHistoryAsync("tenant-a", "definition.asset")).Count);
    }

    [Fact]
    public async Task Restore_replay_requires_the_original_expected_revision_and_payload()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        var draft = await store.CreateDraftAsync(Definition("1.0.0"), expectedRevision: 0);
        var published = await store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision);
        var restored = await store.RestoreAsDraftAsync(
            "tenant-a",
            "definition.asset",
            sourceVersion: "1.0.0",
            draftVersion: "1.1.0",
            expectedRevision: published.Revision);
        var laterDraft = await store.CreateDraftAsync(Definition("2.0.0"), restored.Revision);

        var replay = await store.RestoreAsDraftAsync(
            "tenant-a",
            "definition.asset",
            sourceVersion: "1.0.0",
            draftVersion: "1.1.0",
            expectedRevision: published.Revision);
        var wrongFence = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(() =>
            store.RestoreAsDraftAsync(
                "tenant-a",
                "definition.asset",
                sourceVersion: "1.0.0",
                draftVersion: "1.1.0",
                expectedRevision: restored.Revision).AsTask());
        var wrongPayload = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(() =>
            store.RestoreAsDraftAsync(
                "tenant-a",
                "definition.asset",
                sourceVersion: "1.0.0",
                draftVersion: "1.2.0",
                expectedRevision: published.Revision).AsTask());

        Assert.Equal(restored.Revision, replay.Revision);
        Assert.Equal("records.definition.expected_revision_conflict", wrongFence.Code);
        Assert.Equal(laterDraft.Revision, wrongFence.CurrentRevision);
        Assert.Equal("records.definition.expected_revision_conflict", wrongPayload.Code);
        Assert.Equal(laterDraft.Revision, wrongPayload.CurrentRevision);
        Assert.Equal(4, (await store.ListHistoryAsync("tenant-a", "definition.asset")).Count);
    }

    [Theory]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+build..7")]
    [InlineData("1.0.0+build_7")]
    [InlineData("1.0.0+build+7")]
    public async Task Semantic_versions_reject_malformed_build_metadata(string version)
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());

        var error = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(() =>
            store.CreateDraftAsync(Definition(version), expectedRevision: 0).AsTask());

        Assert.Equal("records.definition.version_invalid", error.Code);
        Assert.Empty(await store.ListHistoryAsync("tenant-a", "definition.asset"));
    }

    [Fact]
    public async Task Published_head_uses_unbounded_numeric_prerelease_precedence()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        const string lower = "1.0.0-alpha.9999999999+lower.001";
        const string higher = "1.0.0-alpha.10000000000+higher.001";
        long revision = 0;
        foreach (var version in new[] { lower, higher })
        {
            var draft = await store.CreateDraftAsync(Definition(version), revision);
            var published = await store.PublishAsync(
                "tenant-a",
                "definition.asset",
                version,
                draft.Revision);
            revision = published.Revision;
        }

        var head = await store.GetPublishedHeadAsync("tenant-a", "definition.asset");

        Assert.NotNull(head);
        Assert.Equal(higher, head.Definition.Envelope.Version);
    }

    [Fact]
    public async Task Semantic_versions_accept_valid_build_metadata_and_unbounded_core_numbers()
    {
        var store = new InMemoryRecordsDefinitionStore(new InMemorySchemaRegistry());
        const string version = "2147483648.0.0-alpha+build.001-x";

        var draft = await store.CreateDraftAsync(Definition(version), expectedRevision: 0);

        Assert.Equal(version, draft.Definition.Envelope.Version);
    }

    [Fact]
    public async Task Failed_publish_leaves_the_draft_and_history_unchanged()
    {
        var store = new InMemoryRecordsDefinitionStore(new RejectingSchemaRegistry());
        var draft = await store.CreateDraftAsync(Definition("1.0.0"), expectedRevision: 0);

        await Assert.ThrowsAsync<InvalidSchemaException>(() =>
            store.PublishAsync(
                "tenant-a",
                "definition.asset",
                "1.0.0",
                expectedRevision: draft.Revision).AsTask());

        Assert.Null(await store.GetPublishedHeadAsync("tenant-a", "definition.asset"));
        var remaining = Assert.Single(
            await store.ListHistoryAsync("tenant-a", "definition.asset"));
        Assert.Equal(RecordsDefinitionStatus.Draft, remaining.Status);
        Assert.Equal(draft.Revision, remaining.Revision);
    }

    [Fact]
    public async Task Publication_is_atomic_against_a_concurrent_edit()
    {
        var registry = new BlockingSchemaRegistry();
        var store = new InMemoryRecordsDefinitionStore(registry);
        var draft = await store.CreateDraftAsync(Definition("1.0.0"), expectedRevision: 0);

        var publishTask = store.PublishAsync(
            "tenant-a",
            "definition.asset",
            "1.0.0",
            expectedRevision: draft.Revision).AsTask();
        await registry.RegistrationStarted;
        var editTask = store.CreateDraftAsync(
            Definition("1.0.0") with { Name = "Concurrent edit" },
            expectedRevision: draft.Revision).AsTask();

        registry.AllowRegistration();

        var published = await publishTask;
        var editConflict = await Assert.ThrowsAsync<RecordsDefinitionConflictException>(
            () => editTask);
        Assert.Equal(RecordsDefinitionStatus.Published, published.Status);
        Assert.Equal("records.definition.version_immutable", editConflict.Code);
        Assert.Equal(2, (await store.ListHistoryAsync("tenant-a", "definition.asset")).Count);
        Assert.NotNull(await store.GetPublishedHeadAsync("tenant-a", "definition.asset"));
    }

    private static RecordTypeDefinition Definition(string version) => new()
    {
        Envelope = new RecordDefinitionEnvelope(
            DefinitionId: "definition.asset",
            Version: version,
            TenantId: "tenant-a",
            PackageId: "package-a",
            Provenance: "test"),
        RecordTypeId = "records.asset",
        Name = "Asset",
        Key = "asset",
        ClassId = "class.master",
        Fields =
        [
            new RecordFieldDefinition
            {
                Name = "Serial number",
                Key = "serial_number",
                Kind = new FieldKindReference(
                    "text",
                    "1.0.0",
                    new Dictionary<string, string> { ["max_length"] = "80" }),
            },
        ],
        Categories = ["equipment"],
    };

    private static void MutateNestedCollections(RecordTypeDefinition definition)
    {
        var categories = Assert.IsAssignableFrom<IList<string>>(definition.Categories);
        categories.Add("caller-mutation");

        var parameters = Assert.IsAssignableFrom<IDictionary<string, string>>(
            Assert.Single(definition.Fields).Kind.Parameters);
        parameters["max_length"] = "999";
    }

    private static void AssertStoredCollectionsAreUnchanged(RecordTypeDefinition definition)
    {
        Assert.Equal(["equipment"], definition.Categories);
        Assert.Equal("80", Assert.Single(definition.Fields).Kind.Parameters["max_length"]);
    }

    private sealed class RejectingSchemaRegistry : ISchemaRegistry
    {
        public ValueTask<Schema?> GetAsync(
            SchemaId id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Schema?>(null);

        public ValueTask<Schema> RegisterAsync(
            string jsonSchemaText,
            IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<Schema>(new InvalidSchemaException("Rejected for test."));

        public ValueTask<SchemaValidationResult> ValidateAsync(
            SchemaId id,
            ReadOnlyMemory<byte> documentBytes,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<Schema> ListAsync(
            string? tagFilter = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class BlockingSchemaRegistry : ISchemaRegistry
    {
        private readonly InMemorySchemaRegistry _inner = new();
        private readonly TaskCompletionSource<bool> _registrationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _registrationAllowed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RegistrationStarted => _registrationStarted.Task;

        public void AllowRegistration() => _registrationAllowed.TrySetResult(true);

        public ValueTask<Schema?> GetAsync(
            SchemaId id,
            CancellationToken cancellationToken = default)
            => _inner.GetAsync(id, cancellationToken);

        public async ValueTask<Schema> RegisterAsync(
            string jsonSchemaText,
            IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            _registrationStarted.TrySetResult(true);
            await _registrationAllowed.Task.WaitAsync(cancellationToken);
            return await _inner.RegisterAsync(jsonSchemaText, parents, tags, cancellationToken);
        }

        public ValueTask<SchemaValidationResult> ValidateAsync(
            SchemaId id,
            ReadOnlyMemory<byte> documentBytes,
            CancellationToken cancellationToken = default)
            => _inner.ValidateAsync(id, documentBytes, cancellationToken);

        public IAsyncEnumerable<Schema> ListAsync(
            string? tagFilter = null,
            CancellationToken cancellationToken = default)
            => _inner.ListAsync(tagFilter, cancellationToken);
    }
}
