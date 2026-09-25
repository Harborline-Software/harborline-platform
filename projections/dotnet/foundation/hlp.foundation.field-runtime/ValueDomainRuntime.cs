using Harborline.Contracts.Fields;
using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Globalization;
using Harborline.Foundation.RuleEngine.Environments;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>Resolves shared field domains and derives their caller-visible editor.</summary>
public sealed class ValueDomainRuntime : IFieldDomainRuntime
{
    private readonly IFieldDomainSource source;
    private readonly IFieldDomainReadAuthority authority;
    private readonly RuleEngineLimits ruleLimits;
    private readonly GuardEvaluator evaluator;

    /// <summary>Uses the host's pinned source, read-authority owner and injected clock.</summary>
    /// <remarks>The clock is required: a predicate may call date.today, and the substrate
    /// never falls back to wall time of its own (the kernel-core clock fence).</remarks>
    public ValueDomainRuntime(IFieldDomainSource source, IFieldDomainReadAuthority authority,
        TimeProvider clock, RuleEngineLimits? ruleLimits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(clock);
        this.source = source;
        this.authority = authority;
        this.ruleLimits = ruleLimits ?? RuleEngineLimits.Default;
        evaluator = new GuardEvaluator(clock, this.ruleLimits);
    }

    // Domain resolution feeds the picker the author sees, so it presents the Render admission.
    private static readonly EvaluationAdmission admission = ValueDomainExpressionEnvironment.Admitted.For(EvaluationPhase.Render);

    /// <inheritdoc />
    public async ValueTask<ResolvedValueDomain> ResolveAsync(ValueDomainDefinition domain,
        FieldDomainScope scope, string jsonPointer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        domain = Detach(domain);
        var snapshot = await OpenSnapshotAsync(scope, jsonPointer, cancellationToken);
        var members = ResolveMembers(domain, snapshot, jsonPointer, cancellationToken);
        var values = await ReadableValuesAsync(domain, members, scope, cancellationToken);
        var kind = SourceKind(domain);
        return new(kind, values, ChooseEditor(kind, values.Count),
            domain.RecordQuery?.Predicate, snapshot.Revision);
    }

    private static FieldEditorKind ChooseEditor(ValueDomainSourceKind kind, int readableCount)
        => readableCount == 0 ? FieldEditorKind.None : readableCount == 1 ? FieldEditorKind.SingleValue
            // Five is runtime presentation policy; the corpus pins three radios and large-query typeahead.
            : readableCount <= 5 ? FieldEditorKind.RadioGroup
            : kind == ValueDomainSourceKind.LiteralSet ? FieldEditorKind.ChoiceList
            : kind == ValueDomainSourceKind.RecordQuery ? FieldEditorKind.RecordPicker : FieldEditorKind.TaxonomyPicker;

    private async ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(FieldDomainScope scope, string jsonPointer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope.Tenant.IsSystemSentinel) throw Refuse("field.value_domain_tenant_required", jsonPointer);
        if (string.IsNullOrWhiteSpace(scope.Principal)) throw Refuse("field.value_domain_principal_required", jsonPointer);
        var snapshot = await source.OpenSnapshotAsync(scope.Tenant, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Tenant != scope.Tenant) throw Refuse("field.value_domain_tenant_mismatch", jsonPointer);
        if (!snapshot.IsComplete || string.IsNullOrWhiteSpace(snapshot.Revision))
            throw Refuse("field.value_domain_snapshot_incomplete", jsonPointer);
        return snapshot;
    }

    private static ValueDomainSourceKind SourceKind(ValueDomainDefinition domain)
        => domain.LiteralValues is not null ? ValueDomainSourceKind.LiteralSet
            : domain.RecordQuery is not null ? ValueDomainSourceKind.RecordQuery : ValueDomainSourceKind.TaxonomyScheme;

    private IReadOnlyList<FieldDomainMember> ResolveMembers(ValueDomainDefinition domain,
        IFieldDomainSnapshot snapshot, string jsonPointer, CancellationToken cancellationToken)
    {
        var refusals = ValueDomainAdmission.Validate(domain, jsonPointer);
        if (refusals.Count > 0) throw new FieldAdmissionException(refusals);
        var members = domain.LiteralValues is { } literals
            ? literals.Select(value => new FieldDomainMember(value, value, default)).ToArray()
            : (domain.RecordQuery is { } query ? snapshot.GetRecords(query.RecordTypeId)
                : snapshot.GetTaxonomyScheme(domain.TaxonomyScheme!))
                ?? throw Refuse("field.value_domain_source_unresolved", jsonPointer);
        cancellationToken.ThrowIfCancellationRequested();
        RuleDefinition? rule = domain.RecordQuery is { } recordQuery ? new()
        {
            Id = "field.domain.predicate", Tier = RuleTier.JsonLogic, Scope = RuleScope.Schema,
            ScopeTarget = "", Action = RuleActionKind.Validate, Expression = recordQuery.Predicate,
        } : null;
        if (rule is not null)
        {
            try { RuleCompiler.Compile([rule], ruleLimits); }
            catch (Exception exception) when (exception is RuleCompilationException or InvalidOperationException)
            {
                throw Refuse("field.value_domain_predicate_invalid", jsonPointer);
            }
        }
        var matching = new List<FieldDomainMember>();
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rule is not null)
            {
                Harborline.Foundation.RuleEngine.Model.Validity validity;
                try
                {
                    var context = member.Fields.EnumerateObject().ToDictionary(property => property.Name,
                        property => JsonNode.Parse(property.Value.GetRawText()), StringComparer.Ordinal);
                    validity = evaluator.EvaluateGuard(rule, RuleContextSnapshot.Capture(context), RuleEvalScope.Root, admission, cancellationToken);
                }
                catch (RuleEngineTimeoutException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    throw Refuse("field.value_domain_predicate_failed", jsonPointer);
                }
                if (!validity.Ok)
                {
                    // GuardEvaluator uses the rule's own id only for an ordinary false verdict.
                    if (validity.Error?.Code == rule.Id) continue;
                    throw Refuse("field.value_domain_predicate_failed", jsonPointer);
                }
            }
            matching.Add(member);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return matching;
    }

    private async ValueTask<IReadOnlyList<string>> ReadableValuesAsync(ValueDomainDefinition domain,
        IReadOnlyList<FieldDomainMember> members, FieldDomainScope scope, CancellationToken cancellationToken)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await authority.CanReadAsync(scope, domain, member, cancellationToken)
                && seen.Add(member.Value)) values.Add(member.Value);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return values.AsReadOnly();
    }

    private static ValueDomainDefinition Detach(ValueDomainDefinition domain)
        => domain with { LiteralValues = domain.LiteralValues is { } values ? Array.AsReadOnly(values.ToArray()) : null };

    private static FieldConstraintDefinition Detach(FieldConstraintDefinition constraint)
        => constraint with
        {
            ReadRoleIds = Array.AsReadOnly(constraint.ReadRoleIds.ToArray()),
            ValueDomain = constraint.ValueDomain is { } domain ? Detach(domain) : null,
        };

    /// <inheritdoc />
    public async ValueTask<ResolvedFieldConstraints> IntersectAsync(IReadOnlyList<FieldConstraintDefinition> constraints,
        FieldDomainScope scope, string jsonPointer, CancellationToken cancellationToken = default)
    {
        constraints = constraints.Select(Detach).ToArray();
        var snapshot = await OpenSnapshotAsync(scope, jsonPointer, cancellationToken);
        // Promoted from RecordsConstraintIntersection.TryResolve; traversal remains Records-owned.
        var required = false;
        var minimum = 0;
        int? maximum = null;
        HashSet<string>? roles = null;
        HashSet<string>? membership = null;
        HashSet<string>? readable = null;
        var sources = new List<FieldDomainAttribution>();
        foreach (var constraint in constraints)
        {
            if (constraint.MinimumCount < 0 || constraint.MaximumCount < 0)
                throw Refuse("field.constraint_intersection_empty", jsonPointer);
            required |= constraint.Required;
            minimum = Math.Max(minimum, constraint.MinimumCount);
            if (constraint.MaximumCount is { } upper)
                maximum = maximum is null ? upper : Math.Min(maximum.Value, upper);
            if (constraint.ReadRoleIds.Count > 0)
            {
                if (roles is null) roles = new(constraint.ReadRoleIds, StringComparer.Ordinal);
                else roles.IntersectWith(constraint.ReadRoleIds);
            }
            if (constraint.ValueDomain is { } domain)
            {
                var members = ResolveMembers(domain, snapshot, jsonPointer, cancellationToken);
                var values = members.Select(member => member.Value);
                if (membership is null) membership = new(values, StringComparer.Ordinal);
                else membership.IntersectWith(values);
                var permitted = await ReadableValuesAsync(domain, members, scope, cancellationToken);
                if (readable is null) readable = new(permitted, StringComparer.Ordinal);
                else readable.IntersectWith(permitted);
                sources.Add(new(SourceKind(domain), domain.TaxonomyScheme, domain.RecordQuery));
            }
        }
        if (maximum < Math.Max(minimum, required ? 1 : 0) || roles is { Count: 0 } || membership is { Count: 0 })
            throw Refuse("field.constraint_intersection_empty", jsonPointer);
        return new(required, minimum, maximum, Array.AsReadOnly(roles?.ToArray() ?? []),
            readable is null ? null : Array.AsReadOnly(readable.ToArray()), sources.AsReadOnly(), snapshot.Revision,
            readable is null ? null : ChooseEditor(sources[0].SourceKind, readable.Count));
    }

    /// <inheritdoc />
    public async ValueTask<ResolvedFieldConstraints> NarrowAsync(FieldConstraintDefinition declared,
        FieldConstraintDefinition narrowed, FieldDomainScope scope, string jsonPointer,
        CancellationToken cancellationToken = default)
    {
        declared = Detach(declared);
        narrowed = Detach(narrowed);
        var snapshot = await OpenSnapshotAsync(scope, jsonPointer, cancellationToken);
        foreach (var constraint in new[] { declared, narrowed })
            if (constraint.MinimumCount < 0 || constraint.MaximumCount < Math.Max(constraint.MinimumCount, constraint.Required ? 1 : 0))
                throw Refuse("field.constraint_intersection_empty", jsonPointer);
        if (declared.Required && !narrowed.Required) throw Refuse("field.requirement_dropped", jsonPointer);
        if (narrowed.MinimumCount < declared.MinimumCount || declared.MaximumCount is { } upper
            && (narrowed.MaximumCount is null || narrowed.MaximumCount > upper))
            throw Refuse("field.multiplicity_widened", jsonPointer);
        if (declared.ReadRoleIds.Count > 0 && (narrowed.ReadRoleIds.Count == 0
            || narrowed.ReadRoleIds.Except(declared.ReadRoleIds, StringComparer.Ordinal).Any()))
            throw Refuse("field.read_roles_widened", jsonPointer);
        if (declared.ValueDomain is not null && narrowed.ValueDomain is null)
            throw Refuse("field.value_domain_widened", jsonPointer);
        var parentMembers = declared.ValueDomain is { } parent
            ? ResolveMembers(parent, snapshot, jsonPointer, cancellationToken) : null;
        var childMembers = narrowed.ValueDomain is { } child
            ? ResolveMembers(child, snapshot, jsonPointer, cancellationToken) : null;
        if (parentMembers is { Count: 0 } || childMembers is { Count: 0 })
            throw Refuse("field.constraint_intersection_empty", jsonPointer);
        if (parentMembers is not null && childMembers is not null)
        {
            var parentValues = new HashSet<string>(parentMembers.Select(member => member.Value), StringComparer.Ordinal);
            if (childMembers.Any(member => !parentValues.Contains(member.Value)))
                throw Refuse("field.value_domain_widened", jsonPointer);
        }
        var domain = narrowed.ValueDomain;
        var readable = domain is null ? null : await ReadableValuesAsync(domain, childMembers!, scope, cancellationToken);
        if (declared.ValueDomain is { } original && readable is not null)
        {
            var parentReadable = await ReadableValuesAsync(original, parentMembers!, scope, cancellationToken);
            readable = Array.AsReadOnly(readable.Intersect(parentReadable, StringComparer.Ordinal).ToArray());
        }
        var sources = new[] { declared.ValueDomain, domain }.OfType<ValueDomainDefinition>()
            .Select(value => new FieldDomainAttribution(SourceKind(value), value.TaxonomyScheme, value.RecordQuery))
            .Distinct().ToArray();
        return new(narrowed.Required, narrowed.MinimumCount, narrowed.MaximumCount,
            Array.AsReadOnly(narrowed.ReadRoleIds.ToArray()), readable,
            Array.AsReadOnly(sources),
            snapshot.Revision, readable is null ? null : ChooseEditor(SourceKind(declared.ValueDomain ?? domain!), readable.Count));
    }

    private static FieldAdmissionException Refuse(string code, string pointer)
        => new([new(code, pointer, "The declared field domain could not be resolved safely.")]);

    /// <inheritdoc />
    public IReadOnlyList<FieldRefusal> Validate(ResolvedFieldConstraints constraints,
        ICompiledFieldKind kind, JsonElement value, string jsonPointer)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(kind);
        var repeated = value.ValueKind == JsonValueKind.Array;
        var count = value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? 0
            : repeated ? value.GetArrayLength() : 1;
        var refusals = new List<FieldRefusal>();
        if (constraints.Required && count == 0)
            refusals.Add(new("field.required", jsonPointer, "A value is required."));
        if (count < constraints.MinimumCount)
            refusals.Add(new("field.minimum_count", jsonPointer, "The value count is below the admitted minimum."));
        if (constraints.MaximumCount is { } maximum && count > maximum)
            refusals.Add(new("field.maximum_count", jsonPointer, "The value count exceeds the admitted maximum."));
        if (count == 0) return refusals;
        var membership = constraints.Values is { } values ? new HashSet<string>(values, StringComparer.Ordinal) : null;
        if (repeated)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                ValidateScalar(item, jsonPointer + "/" + (index++).ToString(CultureInfo.InvariantCulture));
        }
        else ValidateScalar(value, jsonPointer);
        return refusals;

        void ValidateScalar(JsonElement item, string pointer)
        {
            refusals.AddRange(kind.Validate(item, pointer));
            var member = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (membership is not null && (member is null || !membership.Contains(member)))
                refusals.Add(new("field.value_outside_domain", pointer, "The value is outside the permitted field domain."));
        }
    }
}
