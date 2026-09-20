using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.FieldRuntime.Tests;

// These adapters stand in for the external source and authorization owners only.
internal sealed class DomainFixture : IFieldDomainSource, IFieldDomainSnapshot, IFieldDomainReadAuthority
{
    internal static readonly TenantId Tenant = new("tenant-a");
    internal static readonly FieldDomainScope Scope = new(Tenant, "actor-a");
    internal Dictionary<TaxonomySchemeReference, IReadOnlyList<FieldDomainMember>> Schemes { get; } = [];
    internal Dictionary<string, IReadOnlyList<FieldDomainMember>> Records { get; } = [];
    internal HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);
    internal Action? OnOpen { get; set; }
    internal Action? OnRead { get; set; }
    internal Action? OnRecords { get; set; }
    internal Func<ValueDomainDefinition, FieldDomainMember, bool>? CanRead { get; set; }
    internal int Opens { get; private set; }
    public TenantId SnapshotTenant { get; set; } = Tenant;
    TenantId IFieldDomainSnapshot.Tenant => SnapshotTenant;
    public string Revision { get; set; } = "snapshot-1";
    public bool IsComplete { get; set; } = true;
    internal IFieldDomainRuntime Runtime() => new ValueDomainRuntime(this, this, TimeProvider.System);
    internal static FieldDomainMember Member(string value, string fields = "{}")
        => new(value, value, JsonSerializer.Deserialize<JsonElement>(fields));
    public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        if (tenant != Tenant) throw new InvalidOperationException("Unexpected tenant.");
        Opens++;
        OnOpen?.Invoke();
        return ValueTask.FromResult<IFieldDomainSnapshot>(this);
    }
    public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
        => Schemes.GetValueOrDefault(scheme);
    public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId)
    {
        OnRecords?.Invoke();
        return Records.GetValueOrDefault(recordTypeId);
    }
    public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
        FieldDomainMember member, CancellationToken cancellationToken = default)
    {
        OnRead?.Invoke();
        return ValueTask.FromResult(CanRead?.Invoke(domain, member) ?? !Hidden.Contains(member.Value));
    }
}
