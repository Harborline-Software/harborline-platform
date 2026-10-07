using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// The Records definition services are library code: no await in them may resume on the caller's synchronization
/// context, or a host that blocks on the result from a UI or request context deadlocks. Each case makes exactly one
/// awaited store or registry step complete later, on another thread, while every other step completes inline, so the
/// caller's context is still captured when that step is awaited. The technique is kernel.core's KernelAwaitContextTests.
/// </summary>
public sealed class RecordsAwaitContextTests
{
    public static TheoryData<string, string> Steps() => new()
    {
        { "create", "SaveDraftAsync" },
        { "save", "ListHistoryAsync" },
        { "save", "SaveDraftAsync" },
        { "publish", "GetPublicationReplayAsync" },
        { "publish", "ListHistoryAsync" },
        { "publish", "GetPublishedHeadAsync" },
        { "publish", "PublishAsync" },
        { "publish", "RegisterAsync" },
        { "head", "GetPublishedHeadAsync" },
        { "pin", "GetPublishedHeadAsync" },
        { "diagnose", "GetPublishedHeadAsync" },
        { "install", "none" },
        { "field", "ResolvePublishedAsync" },
        { "health", "Records.ListKeysAsync" },
        { "health", "Records.GetPublishedHeadAsync" },
        { "health", "Classes.ListKeysAsync" },
        { "health", "Classes.GetPublishedHeadAsync" },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task no_records_operation_resumes_on_the_callers_context(string operation, string hopAt)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "setup");
        if (operation is "head" or "install" or "field" or "health")
            await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "setup-publish");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new HoppingStore(host.Catalogue, hopAt, release.Task);
        var registry = new HoppingRegistry(host.Registry, hopAt, release.Task);
        var records = new RecordTypeDefinitionStore(store, host.Compiler, host.Defaults, registry, Window);
        var body = (await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"))
            ?? RecordTypeDefinitionJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(AssetClassJson));
        var entry = new RecordTypeDefinitionPackageEntry("eam.asset-class", "1.0.0",
            PlatformPackageContent.PresentJson(RecordTypeDefinitionJson.SerializeCanonical(body)));
        var digest = (await host.Catalogue.ResolvePublishedAsync(new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0"))) is { } published
            ? CatalogueFieldSource.DigestOf(published) : "";

        Func<Task> act = operation switch
        {
            "create" => () => records.CreateDraftAsync(AssetClass("1.0.0") with { Name = "Pump" }, "create").AsTask(),
            "save" => () => records.SaveDraftAsync("eam.asset-class", body, "1.0.0", 1, "save").AsTask(),
            "publish" => () => records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask(),
            "head" => () => records.GetPublishedHeadAsync(Tenant, "eam.asset-class").AsTask(),
            "pin" => () => records.PinReferencesAsync(Tenant, body with { Fields = [.. body.Fields, new("home", "Home",
                Reference: new(null, "eam.equipment", Harborline.Kernel.SchemaValidation.Records.ReferenceCardinality.One,
                    Harborline.Kernel.SchemaValidation.Records.ReferenceDeleteBehavior.Block))] }).AsTask(),
            "diagnose" => () => records.DiagnoseAsync(Tenant, body).AsTask(),
            "install" => () => records.AdmitInstallAsync("tenant-b", entry, NoDependencies).AsTask(),
            "field" => () => new CatalogueFieldSource(store).ResolveAsync(Tenant, new("records.catalogue-field-source", 1,
                DefinitionKind.Records, "eam.asset-class", "1.0.0", "asset_tag", digest, new("tenant"))).AsTask(),
            "health" => () => new RecordsCatalogueHealth(store).ReportAsync(Tenant).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        await NeverResumesOnTheCallersContext(act, release);
        // The delayed step ran exactly once, so the case exercised the await it names.
        Assert.Equal(hopAt == "none" ? 0 : 1, store.Hops + registry.Hops);
    }

    private sealed class CountingContext : SynchronizationContext
    {
        public int Posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    private static async Task NeverResumesOnTheCallersContext(Func<Task> act, TaskCompletionSource release)
    {
        var context = new CountingContext();
        var prior = SynchronizationContext.Current;
        Task running;
        SynchronizationContext.SetSynchronizationContext(context);
        try { running = act(); }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
            // Complete only after act has returned: its selected await must observe an incomplete task
            // while the counting context is installed, regardless of thread scheduling or machine load.
            release.SetResult();
        }
        await running;
        Assert.Equal(0, context.Posts);
    }

    private sealed class HoppingStore(IVersionedDefinitionStore inner, string hopAt, Task release) : IVersionedDefinitionStore
    {
        public int Hops;

        // Only the first call of the named step completes later; every other step completes inline.
        private async Task Hop(string step, DefinitionKind? kind = null)
        {
            if (step != hopAt && $"{kind}.{step}" != hopAt) return;
            if (Interlocked.CompareExchange(ref Hops, 1, 0) != 0) return;
            await release.ConfigureAwait(false);
        }

        public async ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default)
        { await Hop(nameof(ListKeysAsync), kind).ConfigureAwait(false); return await inner.ListKeysAsync(tenant, kind, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        { await Hop(nameof(SaveDraftAsync)).ConfigureAwait(false); return await inner.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        { await Hop(nameof(PublishAsync)).ConfigureAwait(false); return await inner.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
            string requestId, IReadOnlyList<DefinitionPublishedHeadCondition> conditions, CancellationToken cancellationToken = default)
        { await Hop(nameof(PublishAsync)).ConfigureAwait(false); return await inner.PublishAsync(key, versionId, expectedRevision, requestId, conditions, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<DefinitionRevision?> GetPublicationReplayAsync(DefinitionKey key, string versionId, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default)
        { await Hop(nameof(GetPublicationReplayAsync)).ConfigureAwait(false); return await inner.GetPublicationReplayAsync(key, versionId, expectedRevision, requestId, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        { await Hop(nameof(RestoreAsDraftAsync)).ConfigureAwait(false); return await inner.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision, requestId, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default)
        { await Hop(nameof(ListHistoryAsync)).ConfigureAwait(false); return await inner.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default)
        { await Hop(nameof(GetPublishedHeadAsync), key.Kind).ConfigureAwait(false); return await inner.GetPublishedHeadAsync(key, cancellationToken).ConfigureAwait(false); }

        public async ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default)
        { await Hop(nameof(ResolvePublishedAsync)).ConfigureAwait(false); return await inner.ResolvePublishedAsync(binding, cancellationToken).ConfigureAwait(false); }
    }

    private sealed class HoppingRegistry(ISchemaRegistry inner, string hopAt, Task release) : ISchemaRegistry
    {
        public int Hops;

        public ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);

        public async ValueTask<Schema> RegisterAsync(string jsonSchemaText, IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
        {
            if (hopAt == nameof(RegisterAsync) && Interlocked.CompareExchange(ref Hops, 1, 0) == 0) await release.ConfigureAwait(false);
            return await inner.RegisterAsync(jsonSchemaText, parents, tags, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<SchemaValidationResult> ValidateAsync(SchemaId id, ReadOnlyMemory<byte> documentBytes, CancellationToken cancellationToken = default)
            => inner.ValidateAsync(id, documentBytes, cancellationToken);

        public IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null, CancellationToken cancellationToken = default)
            => inner.ListAsync(tagFilter, cancellationToken);
    }
}
