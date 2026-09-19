using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;

internal static class FieldRuntimeDomainProbe
{
    internal static async Task VerifyAsync()
    {
        var source = new PinnedSource();
        IFieldDomainRuntime runtime = new ValueDomainRuntime(source, source);
        var scope = new FieldDomainScope(source.Tenant, "package-reader");
        var taxonomy = new ValueDomainDefinition(TaxonomyScheme: new("status", "1"));
        const string predicate = "{\"var\":\"eligible\"}";
        var query = new ValueDomainDefinition(RecordQuery: new("status-record", predicate));

        var taxonomyResult = await runtime.ResolveAsync(taxonomy, scope, "/status");
        var queryResult = await runtime.ResolveAsync(query, scope, "/status");
        if (!taxonomyResult.Values.SequenceEqual(new[] { "allowed" })
            || !queryResult.Values.SequenceEqual(new[] { "allowed" })
            || taxonomyResult.SourceKind != ValueDomainSourceKind.TaxonomyScheme
            || queryResult.SourceKind != ValueDomainSourceKind.RecordQuery
            || queryResult.Predicate != predicate
            || queryResult.SnapshotRevision != "package-snapshot-1")
            throw new InvalidOperationException("Packed field domains lost predicate evaluation, authority filtering, or source attribution.");

        var floor = new FieldConstraintDefinition(true, 0, 1, Array.Empty<string>(), taxonomy);
        var narrowed = await runtime.NarrowAsync(floor, floor with { ValueDomain = query }, scope, "/status");
        if (!narrowed.Required || !narrowed.Values!.SequenceEqual(new[] { "allowed" }))
            throw new InvalidOperationException("Packed field domain narrowing did not resolve cross-source membership.");

        var kinds = new FieldKindRuntime(new FieldKindRegistry(new[]
        {
            new AdmittedFieldKind("status", "1", null, FieldScalarValueShape.Text),
        }));
        var kind = kinds.Bind(new("status", "1", new Dictionary<string, string>()), "/status");
        if (runtime.Validate(narrowed, kind, JsonSerializer.SerializeToElement("allowed"), "/status").Count != 0)
            throw new InvalidOperationException("Packed field validation refused a readable permitted value.");
        if (!runtime.Validate(narrowed, kind, default, "/status").Any(refusal =>
            refusal.Code == "field.required" && refusal.JsonPointer == "/status"))
            throw new InvalidOperationException("Packed field validation dropped the required floor.");
        var repeated = JsonSerializer.SerializeToElement(new[] { "allowed", "hidden" });
        var errors = runtime.Validate(narrowed, kind, repeated, "/a~0~1b");
        if (!errors.Any(refusal => refusal.Code == "field.maximum_count" && refusal.JsonPointer == "/a~0~1b")
            || !errors.Any(refusal => refusal.Code == "field.value_outside_domain" && refusal.JsonPointer == "/a~0~1b/1")
            || repeated[1].GetString() != "hidden")
            throw new InvalidOperationException("Packed field validation lost count, membership, pointer, or non-mutation semantics.");

        var refusedWidening = false;
        try
        {
            await runtime.NarrowAsync(floor, floor with
            {
                ValueDomain = new(LiteralValues: new[] { "allowed", "hidden", "outside" }),
            }, scope, "/a~0~1b");
        }
        catch (FieldAdmissionException error)
        {
            refusedWidening = error.Refusals.Any(refusal =>
                refusal.Code == "field.value_domain_widened" && refusal.JsonPointer == "/a~0~1b");
        }
        if (!refusedWidening)
            throw new InvalidOperationException("Packed field domain accepted widening concealed by read authority.");

        var refusedRevision = false;
        try
        {
            await runtime.ResolveAsync(new(TaxonomyScheme: new("status", "2")), scope, "/status");
        }
        catch (FieldAdmissionException error)
        {
            refusedRevision = error.Refusals.Any(refusal =>
                refusal.Code == "field.value_domain_source_unresolved" && refusal.JsonPointer == "/status");
        }
        if (!refusedRevision)
            throw new InvalidOperationException("Packed field domain silently substituted another taxonomy revision.");

        Console.WriteLine("FIELD_RUNTIME_DOMAIN_PACKAGE_PASS: exact taxonomy revision, query predicate, read authority, complete-membership narrowing, shared required/count/membership validation");
    }

    private sealed class PinnedSource : IFieldDomainSource, IFieldDomainSnapshot, IFieldDomainReadAuthority
    {
        private readonly IReadOnlyList<FieldDomainMember> taxonomy = new[]
        {
            Member("allowed", true), Member("hidden", true),
        };
        private readonly IReadOnlyList<FieldDomainMember> records = new[]
        {
            Member("allowed", true), Member("hidden", true), Member("excluded", false),
        };

        public TenantId Tenant { get; } = new("package-tenant");
        public string Revision => "package-snapshot-1";
        public bool IsComplete => true;

        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant, CancellationToken cancellationToken = default)
        {
            if (tenant != Tenant) throw new InvalidOperationException("Unexpected package-consumer tenant.");
            return ValueTask.FromResult<IFieldDomainSnapshot>(this);
        }

        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
            => scheme == new TaxonomySchemeReference("status", "1") ? taxonomy : null;

        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId)
            => recordTypeId == "status-record" ? records : null;

        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
            FieldDomainMember member, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(scope.Tenant == Tenant && scope.Principal == "package-reader"
                && member.Value is "allowed" or "excluded");

        private static FieldDomainMember Member(string value, bool eligible)
            => new(value, value, JsonSerializer.SerializeToElement(new { eligible }));
    }
}
