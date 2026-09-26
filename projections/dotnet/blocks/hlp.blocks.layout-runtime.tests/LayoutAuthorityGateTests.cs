using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.RuleEngine;
using Xunit;

namespace Harborline.Blocks.LayoutRuntime.Tests;

/// <summary>
/// T-583 slice 2: the open and submit gates at the platform boundary and the authority a returned surface
/// carries (DES-0052 layout-eng-26, §9 rows C1 and A6), and T-724 ruling 76's submit proof.
/// </summary>
public sealed class LayoutAuthorityGateTests
{
    private static readonly DefinitionKey Key = new("tenant-a", DefinitionKind.Layout, "surface.customer-edit");

    [Fact(DisplayName = "layout-eng-26: a surface the reader may not open is refused at the platform boundary before the store is read")]
    public async Task ASurfaceTheReaderMayNotOpenIsRefusedBeforeTheStoreIsRead()
    {
        var store = await Published();
        var reader = new Authority(open: false);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(Key, "version-1"), reader, reader));
        Assert.Equal((DefinitionAdmissionPhase.Render, LayoutAuthorityCodes.OpenForbidden, ""),
            (refused.Stage, Assert.Single(refused.Refusals).Code, refused.Refusals[0].Pointer));
        Assert.Equal(["surface.customer-edit"], reader.Opened);

        // Refused before the read: a version that does not exist refuses identically, so a denial reveals nothing.
        var missing = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(Key, "version-9"), reader, reader));
        Assert.Equal(refused.Refusals, missing.Refusals);

        // Both ports are required: a missing one fails rather than opening or deciding authority.
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(Key, "version-1"), null!, reader));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(Key, "version-1"), reader, null!));

        var opened = await new LayoutPublishedSurfaceResolver(store).ResolveAsync(new(Key, "version-1"), new Authority(), new Authority());
        Assert.Equal("surface.customer-edit", opened.Definition.Envelope.Identity);
        Assert.True(opened.Authority.CanSubmit);
    }

    [Fact(DisplayName = "layout-eng-26: a full-data, deny-all authority opens the surface read-only with no submit, matching the shared lane fixture")]
    public async Task AFullDataDenyAllAuthorityOpensReadOnlyWithNoSubmit()
    {
        // DES-0052 §9 A6: authority denies every read, the gate and the write, while the sources return full
        // data. Open is allowed, or there would be no surface to render at all.
        var denyAll = new Authority(read: false, gate: false, write: false);
        var surface = await new LayoutPublishedSurfaceResolver(await Published()).ResolveAsync(new(Key, "version-1"), denyAll, denyAll);

        Assert.True(surface.Authority.ReadOnly);
        Assert.False(surface.Authority.CanSubmit);
        var submit = Assert.Throws<DefinitionRefusalException>(() => LayoutAuthorityGate.RequireSubmit(surface.Definition, denyAll));
        Assert.Equal((DefinitionAdmissionPhase.Render, LayoutAuthorityCodes.SubmitForbidden), (submit.Stage, Assert.Single(submit.Refusals).Code));

        // Full data, nothing read: every set-scoped block resolves empty (layout-eng-15).
        var resolution = new LayoutBindingResolver(new GuardEvaluator(TimeProvider.System)).Resolve(
            surface.Definition, new FullData(), LayoutBindingScope.Root(new Dictionary<string, JsonNode?> { ["customer.name"] = "Ada" }),
            new NoTrace(), new("request-1", "principal.clerk-4"), denyAll);
        Assert.All(resolution.Blocks.Where(block => block.BindingKind != LayoutBindingKinds.RecordField), block => Assert.Null(block.Value));

        // The one platform model the React and Blazor probes render: same flow, same authority.
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "deny-all-authority.json")));
        Assert.Equal(fixture.RootElement.GetProperty("definitionId").GetString(), surface.Definition.Envelope.Identity);
        Assert.Equal(
            fixture.RootElement.GetProperty("flow").EnumerateArray().Select(block => (block.GetProperty("id").GetString(), block.GetProperty("kind").GetString(), block.GetProperty("depth").GetInt32())),
            surface.Plan.Flow.Select(block => ((string?)block.BlockId, (string?)block.Kind, block.Depth)));
        Assert.Equal(fixture.RootElement.GetProperty("authority").GetProperty("canSubmit").GetBoolean(), surface.Authority.CanSubmit);
    }

    [Fact(DisplayName = "T-724 ruling 76: a submit needs both the surface's submit gate and the existing Records write check; either alone refuses")]
    public void ASubmitNeedsBothTheSubmitGateAndTheRecordsWriteCheck()
    {
        var gated = Surface();
        foreach (var (gate, write, allowed) in new[] { (true, true, true), (true, false, false), (false, true, false), (false, false, false) })
        {
            var submitter = new Authority(gate: gate, write: write);
            Assert.Equal(allowed, LayoutAuthorityGate.Authority(gated, submitter).CanSubmit);
            if (allowed) LayoutAuthorityGate.RequireSubmit(gated, submitter);
            else Assert.Equal(LayoutAuthorityCodes.SubmitForbidden,
                Assert.Single(Assert.Throws<DefinitionRefusalException>(() => LayoutAuthorityGate.RequireSubmit(gated, submitter)).Refusals).Code);
        }
        // The gate is asked with the surface's own gate, and nothing else stands in for it.
        var asked = new Authority();
        LayoutAuthorityGate.RequireSubmit(gated, asked);
        Assert.Equal([gated.SubmitGate!], asked.Gates);

        // A capture surface with no declared gate still needs the Records write check,
        var ungated = gated with { SubmitGate = null };
        Assert.True(LayoutAuthorityGate.Authority(ungated, new Authority(gate: false)).CanSubmit);
        Assert.False(LayoutAuthorityGate.Authority(ungated, new Authority(write: false)).CanSubmit);
        // and a surface that is not capture-dominant never submits.
        Assert.False(LayoutAuthorityGate.Authority(ungated with { DefaultIntent = LayoutIntent.Observe }, new Authority()).CanSubmit);
    }

    private static async Task<InMemoryVersionedDefinitionStore> Published()
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission> { [DefinitionKind.Layout] = (_, _) => [] });
        await store.SaveDraftAsync(new(Key, "version-1", "1.0.0", Encoding.UTF8.GetString(LayoutDefinitionJson.SerializeCanonical(Surface()))), 0, "draft-1");
        await store.PublishAsync(Key, "version-1", 1, "publish-1");
        return store;
    }

    private static LayoutDefinition Surface() => new(
        new("surface.customer-edit", "1.0.0", "tenant-a", LayoutCascadeLayer.TenantConfiguration,
            JsonSerializer.SerializeToElement(new { source = "t-583" }), "standard", false,
            [new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "1.0.0")]),
        1, LayoutMedium.Screen, LayoutIntent.Capture,
        [
            new LayoutBlock("name", "layout.field", new LayoutRecordFieldBinding("customer.name"), []),
            new LayoutBlock("orders", "layout.table", new LayoutQueryBinding("views.orders"), [], Intent: LayoutIntent.Observe),
            new LayoutBlock("orders-total", "layout.metric", new LayoutMeasureBinding("orders.total"), [], Intent: LayoutIntent.Observe),
        ],
        [], [], [], new LayoutSubmitGate(Role: RoleReference.Domain("customer-editor")), []);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The platform repository root was not found above the test output.");
    }

    /// <summary>The principal's Access for one surface and record, as the host builds it; every answer is fixed.</summary>
    private sealed class Authority(bool open = true, bool read = true, bool gate = true, bool write = true) : ILayoutAccess, ILayoutSubmitAccess
    {
        public List<string> Opened { get; } = [];

        public List<LayoutSubmitGate> Gates { get; } = [];

        public bool CanRead(LayoutBinding binding) => read;

        public bool CanOpen(string surfaceId)
        {
            Opened.Add(surfaceId);
            return open;
        }

        public bool Satisfies(LayoutSubmitGate submitGate)
        {
            Gates.Add(submitGate);
            return gate;
        }

        public bool CanWrite() => write;
    }

    /// <summary>A source that would return every row and total it is asked for.</summary>
    private sealed class FullData : ILayoutBindingSources
    {
        public LayoutFieldResult ResolveField(LayoutBindingScope scope, string fieldPath)
            => scope.Values.TryGetValue(fieldPath, out var value) ? LayoutFieldResult.Resolved(value) : LayoutFieldResult.Undeclared;

        public bool TryResolveQuery(LayoutBindingScope scope, string viewDefinitionId, out JsonNode? value)
        {
            value = new JsonArray(new JsonObject { ["id"] = "order-1" }, new JsonObject { ["id"] = "order-2" });
            return true;
        }

        public bool TryResolveMeasure(LayoutBindingScope scope, string measurePath, out JsonNode? value)
        {
            value = JsonValue.Create(1200);
            return true;
        }

        public bool TryResolveTemplate(LayoutBindingScope scope, string templateDefinitionId, out JsonNode? value)
        {
            value = null;
            return false;
        }

        public bool TryResolveCollection(LayoutBindingScope scope, string name, out IReadOnlyList<JsonNode?> rows)
        {
            rows = [];
            return true;
        }

        public LayoutRelatedResult ResolveRelated(LayoutBindingScope scope, string relationship) => LayoutRelatedResult.Undeclared;
    }

    private sealed class NoTrace : ILayoutDecisionTrace
    {
        public void RecordDenial(LayoutRelatedDenial denial) => throw new InvalidOperationException("No related binding is denied here.");

        public void RecordFieldDenial(LayoutFieldDenial denial) => throw new InvalidOperationException("No field read is denied here.");
    }
}
