using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;
using System.Text.Json;

namespace Harborline.Kernel.SchemaValidation.Tests;

internal static class RecordsTestDomains
{
    internal static readonly TenantId Tenant = new("tenant-a");
    internal static readonly FieldDomainScope Scope = new(Tenant, "records-test-actor");

    internal static IFieldDomainRuntime CreateRuntime()
        => CreateRuntime(
            new Dictionary<TaxonomySchemeReference, IReadOnlyList<FieldDomainMember>>(),
            new Dictionary<string, IReadOnlyList<FieldDomainMember>>());

    internal static IFieldDomainRuntime CreateRuntime(
        IReadOnlyDictionary<TaxonomySchemeReference, IReadOnlyList<FieldDomainMember>> schemes,
        IReadOnlyDictionary<string, IReadOnlyList<FieldDomainMember>> records)
        => new ValueDomainRuntime(new CompleteSource(schemes, records), new ReadAuthority(), TimeProvider.System);

    internal static IFieldDomainRuntime CreateRuntime(
        IFieldDomainSource source,
        IFieldDomainReadAuthority authority)
        => new ValueDomainRuntime(source, authority, TimeProvider.System);

    internal static FieldDomainMember Member(string value, string fields = "{}")
        => new(value, value, JsonSerializer.Deserialize<JsonElement>(fields));

    private sealed class CompleteSource(
        IReadOnlyDictionary<TaxonomySchemeReference, IReadOnlyList<FieldDomainMember>> schemes,
        IReadOnlyDictionary<string, IReadOnlyList<FieldDomainMember>> records) : IFieldDomainSource
    {
        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(
            TenantId tenant,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IFieldDomainSnapshot>(new CompleteSnapshot(tenant, schemes, records));
        }
    }

    private sealed class CompleteSnapshot(
        TenantId tenant,
        IReadOnlyDictionary<TaxonomySchemeReference, IReadOnlyList<FieldDomainMember>> schemes,
        IReadOnlyDictionary<string, IReadOnlyList<FieldDomainMember>> records) : IFieldDomainSnapshot
    {
        public TenantId Tenant => tenant;
        public string Revision => "records-tests-1";
        public bool IsComplete => true;

        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
            => schemes.GetValueOrDefault(scheme);

        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId)
            => records.GetValueOrDefault(recordTypeId);
    }

    private sealed class ReadAuthority : IFieldDomainReadAuthority
    {
        public ValueTask<bool> CanReadAsync(
            FieldDomainScope scope,
            ValueDomainDefinition domain,
            FieldDomainMember member,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }
}
