using System.Text.Json;
using Harborline.Blocks.EntityViews;
using Harborline.Contracts.Fields;
using Harborline.Foundation.DataExchange;
using Harborline.Foundation.FieldRuntime;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class SharedFieldCrossCallerTests
{
    [Fact]
    public async Task Real_forms_views_and_exchange_proposals_share_the_same_authorized_domain()
    {
        var fields = new SharedFieldBindingTests.FieldHost();
        var form = await SharedFieldBindingTests.Create(fields);
        var runtime = new ValueDomainRuntime(fields, fields, TimeProvider.System);
        var viewHost = new ViewHost(fields);
        var views = new ViewQueryRuntime(viewHost, viewHost, viewHost, viewHost, viewHost,
            new InMemoryViewRowSource([new("1", new Dictionary<string, object?> { ["name"] = "allowed" })]), viewHost, TimeProvider.System, runtime);
        var rendered = await form.Engine.RenderAsync(form.Definition.Id, null);
        var result = await views.ExecuteAsync(new("tenant-engine", "view", "alice", new(0, 10), new("table", new Dictionary<ViewShapeRole, string>())));
        Assert.Equal(new[] { "allowed" }, rendered.Sections[0].Fields[0].PermittedValues.Value);
        Assert.Equal(new[] { "allowed" }, result.ColumnDomains["name"].Values);
        Assert.Equal("allowed", Assert.Single(result.Rows).Values["name"]);

        var context = new ExchangeEvaluationContext("alice", "source", "window", null, "decision", "standard");
        var payloads = new CheckedPayloadStore(runtime, fields.Domain!, new(fields.Tenant, context.RequestedBy));
        var capabilities = new SourceHost();
        var interpreter = new DataExchangeInterpreter(capabilities,
            new DataExchangeRuntime(new InMemoryExchangeRunStore(), TimeProvider.System, capabilities), payloads);
        var definition = new DataExchangeDefinition("tenant-engine", "import", "1", "Import",
            new("source", "1", "secret://source", new Dictionary<string, string>()),
            new(TabularMappingProfile.Family, TabularMappingProfile.SchemaUri, "1.0.0", new("records.model/v1", "/model"),
                [new("Name", "string", true, "/model/name")], new Dictionary<string, string>()),
            ReplayPolicy.AppendDeduplicate, ["Name"], null);
        var dryRun = await interpreter.CreateDryRunAsync(definition, context);
        var effect = Assert.Single(dryRun.NormalizedEffects);
        Assert.Equal("mapping.proposed", Assert.Single(dryRun.Evaluations).Outcome.Code);
        var payload = await payloads.GetAsync(effect.PayloadReference!);
        Assert.Equal("allowed", payload!.Values["/model/name"]);
        Assert.Equal(new[] { "allowed" }, payload.Values.Values.Cast<string>());

        foreach (var forbidden in new[] { "hidden", "outside" })
        {
            capabilities.Value = forbidden;
            var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await interpreter.CreateDryRunAsync(definition, context));
            Assert.Equal("field.value_outside_domain", Assert.Single(error.Refusals).Code);
        }
        Assert.Single(payloads.StoredReferences);
        Assert.Equal(new[] { "alice" }, fields.Actors.Distinct());
    }

    // Independent host composition only: the production interpreter still owns mapping and proposal creation.
    private sealed class CheckedPayloadStore(IFieldDomainRuntime runtime, ValueDomainDefinition domain, FieldDomainScope scope) : IProtectedEffectPayloadStore
    {
        private readonly InMemoryProtectedEffectPayloadStore inner = new();
        private readonly ICompiledFieldKind kind = new FieldKindRuntime(new FieldKindRegistry(
            [new("mapped-text", "1", null)])).Bind(new("mapped-text", "1", new Dictionary<string, string>()), "/model/name");
        public List<string> StoredReferences { get; } = [];
        public async ValueTask<string> SaveAsync(DryRunId dryRunId, int sourceOrdinal, CanonicalEffectPayload payload, CancellationToken cancellationToken = default)
        {
            if (payload.TargetContract != "records.model/v1" || !payload.Values.TryGetValue("/model/name", out var value)
                || value is null)
                throw new FieldAdmissionException([new("field.binding_unresolved", "/model/name", "The mapped field binding is not admitted.")]);
            var constraints = await runtime.IntersectAsync([new(true, 0, 1, [], domain)], scope, "/model/name", cancellationToken);
            var refusals = runtime.Validate(constraints, kind, JsonSerializer.SerializeToElement(value), "/model/name");
            if (refusals.Count > 0) throw new FieldAdmissionException(refusals);
            var reference = await inner.SaveAsync(dryRunId, sourceOrdinal, payload, cancellationToken);
            StoredReferences.Add(reference);
            return reference;
        }
        public ValueTask<CanonicalEffectPayload?> GetAsync(string reference, CancellationToken cancellationToken = default) => inner.GetAsync(reference, cancellationToken);
    }

    private sealed class SourceHost : IDataExchangeCapabilityRegistry, IReadOnlyAcquisitionSource, IRunLifecyclePolicyPort
    {
        public string Value { get; set; } = "allowed";
        public string CapabilityId => "source";
        public string ConnectorVersion => "1";
        public IReadOnlyAcquisitionSource ResolveSource(string capabilityId, string connectorVersion) => this;
        public INamedMappingTransform ResolveTransform(string name) => throw new NotSupportedException();
        public bool CanWrite(string targetContract, string targetPointer) => targetContract == "records.model/v1" && targetPointer == "/model/name";
        public ValueTask<DiscoveredSourceShape> DiscoverAsync(ExchangeSourceBinding binding, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DiscoveredSourceShape([new("Name", "string")]));
        public async IAsyncEnumerable<AcquiredSourceRecord> ReadAsync(ExchangeSourceBinding binding, string inputBoundary,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(0, "row", "1", "end", new Dictionary<string, string?> { ["Name"] = Value });
            await Task.CompletedTask;
        }
        public ValueTask<RunRetention> DeriveAsync(string tenantId, string retentionClass, DateTimeOffset requestedAt, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new RunRetention(requestedAt.AddDays(1), false));
    }

    private sealed class ViewHost(SharedFieldBindingTests.FieldHost fields) : IViewDefinitionSource, IViewOpenGate, IViewKindRegistry,
        IViewRecordTypeRegistry, IViewAccessFilter, IViewMeasureCatalog
    {
        public ValueTask<ViewDefinition?> ResolvePublishedHeadAsync(string tenant, string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ViewDefinition?>(new(new(key, "1", tenant, ViewCascadeLayer.Base, JsonSerializer.SerializeToElement(new { }), []),
                1, "View", "model", ViewOwnershipTier.System, "read", new([new("name", 100)], [], null, null, null)));
        public ValueTask<ViewAuthority> AuthorizeAsync(ViewDefinition definition, string principal, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ViewAuthority(principal == "alice", []));
        ValueTask<ViewKindDescriptor?> IViewKindRegistry.ResolveAsync(string kind, CancellationToken cancellationToken)
            => ValueTask.FromResult<ViewKindDescriptor?>(new(kind, "renderer", []));
        public ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ViewKindDescriptor>>([]);
        public ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(string recordType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(string tenant, string recordType, CancellationToken cancellationToken = default)
        {
            var model = await fields.ResolveAsync(new(tenant), fields.SchemaRef, cancellationToken);
            return new(recordType, new Dictionary<string, ViewRecordFieldKind> { ["name"] = ViewRecordFieldKind.Text }, tenant, model!.SchemaRef, model.Fields);
        }
        public ValueTask<ViewFilter> BuildAsync(string tenant, string principal, string recordType, DateTimeOffset at, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ViewFilter.Equal("name", "allowed"));
        ValueTask<ViewMeasureDescriptor?> IViewMeasureCatalog.ResolveAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ViewMeasureResult> EvaluateAsync(ViewMeasureBinding binding, IReadOnlyList<ViewRow> rows, DateTimeOffset evaluatedAt, string tenant, string principal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
