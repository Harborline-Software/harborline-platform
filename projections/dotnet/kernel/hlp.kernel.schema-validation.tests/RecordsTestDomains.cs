using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;

namespace Harborline.Kernel.SchemaValidation.Tests;

internal static class RecordsTestDomains
{
    internal static IFieldDomainRuntime CreateRuntime()
        => new ValueDomainRuntime(new CompleteSource(), new ReadAuthority(), TimeProvider.System);

    private sealed class CompleteSource : IFieldDomainSource
    {
        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(
            TenantId tenant,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IFieldDomainSnapshot>(new CompleteSnapshot(tenant));
        }
    }

    private sealed class CompleteSnapshot(TenantId tenant) : IFieldDomainSnapshot
    {
        public TenantId Tenant => tenant;
        public string Revision => "records-tests-1";
        public bool IsComplete => true;

        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
            => null;

        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId)
            => null;
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
