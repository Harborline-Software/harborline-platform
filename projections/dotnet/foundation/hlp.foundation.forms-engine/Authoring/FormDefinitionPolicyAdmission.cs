using Harborline.Contracts.Authorization;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Forms.Engine.Security;

namespace Harborline.Foundation.Forms.Engine.Authoring;

/// <summary>Fail-closed policy admission run by an authoring host before publication.</summary>
public interface IFormDefinitionPolicyAdmission
{
    /// <summary>Validates every authored field and connector input against the runtime policy model.</summary>
    void ValidateOrThrow(FormDefinition definition);
}

/// <summary>
/// Bounded destination policy admission. It validates the same form/section/container/field grains
/// consumed by the runtime resolver without importing the aggregate legacy Governance topology.
/// </summary>
public sealed class DefaultFormDefinitionPolicyAdmission(
    IFormFieldGovernanceResolver governance) : IFormDefinitionPolicyAdmission
{
    public void ValidateOrThrow(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        foreach (var fieldName in definition.Overlay.Fields.Keys)
        {
            ValidateMonotonicGrains(definition, fieldName);
            var policy = governance.Resolve(definition, fieldName);
            if (!policy.IsResolved)
            {
                throw Fail(
                    definition,
                    fieldName,
                    policy.Refusal == FormGovernanceRefusal.PolicyInvalid
                        ? "aspect.policy_invalid"
                        : "aspect.policy_unresolved");
            }
        }

        foreach (var check in definition.Overlay.AsyncChecks ?? [])
        {
            if (check.AllowsSensitiveInputs) continue;
            foreach (var fieldName in new[] { check.Field }.Concat(check.Inputs ?? []).Distinct(StringComparer.Ordinal))
            {
                if (!definition.Overlay.Fields.TryGetValue(fieldName, out var field)) continue;
                var policy = governance.Resolve(definition, fieldName);
                if (field.PiiSensitivity == PiiSensitivity.Sensitive || policy.ProtectAtRest)
                {
                    throw new FormDefinitionValidationException(
                        definition.Id,
                        $"aspect.sensitive_input_unacknowledged: async check '{check.Id}' feeds classified field '{fieldName}' to connector '{check.Connector}' without allowsSensitiveInputs.");
                }
            }
        }
    }

    private static void ValidateMonotonicGrains(FormDefinition definition, string fieldName)
    {
        var grains = EffectiveAspects(definition.Overlay, fieldName).ToArray();
        HashSet<string>? inheritedTags = null;
        HashSet<RoleReference>? inheritedReadRoles = null;
        HashSet<RoleReference>? inheritedWriteRoles = null;
        int? retentionDays = null;
        Immutability? immutability = null;
        HashSet<string>? allowedResidency = null;
        var prohibitedResidency = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grain in grains)
        {
            if (grain.Classification is { } classification)
            {
                var declared = classification.Tags.Select(TagKey).ToHashSet(StringComparer.Ordinal);
                if (classification.Tags.Any(tag => string.IsNullOrWhiteSpace(tag.System) || string.IsNullOrWhiteSpace(tag.Code)))
                    throw Fail(definition, fieldName, "aspect.policy_invalid");
                if (inheritedTags is not null && !inheritedTags.IsSubsetOf(declared))
                    throw Fail(definition, fieldName, "aspect.relax_forbidden");
                inheritedTags = declared;
            }

            inheritedReadRoles = NarrowRoles(definition, fieldName, inheritedReadRoles, grain.Access?.ReadRoles);
            inheritedWriteRoles = NarrowRoles(definition, fieldName, inheritedWriteRoles, grain.Access?.WriteRoles);

            if (grain.Lifecycle?.Retention is { } retention)
            {
                if (string.IsNullOrWhiteSpace(retention.Regime) ||
                    string.IsNullOrWhiteSpace(retention.FloorClass) ||
                    retention.MinimumRetentionDays < 0)
                    throw Fail(definition, fieldName, "aspect.policy_invalid");
                if (retentionDays is not null && retention.MinimumRetentionDays < retentionDays)
                    throw Fail(definition, fieldName, "aspect.relax_forbidden");
                retentionDays = retention.MinimumRetentionDays;
            }

            if (grain.Lifecycle is { } lifecycle)
            {
                if (immutability is not null && (int)lifecycle.Immutability < (int)immutability.Value)
                    throw Fail(definition, fieldName, "aspect.relax_forbidden");
                immutability = lifecycle.Immutability;
            }

            if (grain.Lifecycle?.Residency is { } residency)
            {
                if (residency.AllowedJurisdictions.Any(string.IsNullOrWhiteSpace) ||
                    residency.ProhibitedJurisdictions?.Any(string.IsNullOrWhiteSpace) == true)
                    throw Fail(definition, fieldName, "aspect.policy_invalid");
                var declared = residency.AllowedJurisdictions.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var effectiveAllowed = allowedResidency is null
                    ? declared
                    : allowedResidency.Intersect(declared, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var prohibited in residency.ProhibitedJurisdictions ?? []) prohibitedResidency.Add(prohibited);
                effectiveAllowed.ExceptWith(prohibitedResidency);
                if (effectiveAllowed.Count == 0)
                    throw Fail(definition, fieldName, "aspect.residency_unsatisfiable");
                allowedResidency = effectiveAllowed;
            }
        }
    }

    private static HashSet<RoleReference>? NarrowRoles(
        FormDefinition definition,
        string fieldName,
        HashSet<RoleReference>? inherited,
        IReadOnlyList<RoleReference>? declaredRoles)
    {
        if (declaredRoles is null) return inherited;
        var declared = declaredRoles.ToHashSet();
        if (declaredRoles.Any(role => string.IsNullOrWhiteSpace(role.Vocabulary) || string.IsNullOrWhiteSpace(role.Name)) || declared.Count != declaredRoles.Count)
            throw Fail(definition, fieldName, "aspect.policy_invalid");
        if (inherited is not null && !declared.IsSubsetOf(inherited))
            throw Fail(definition, fieldName, "aspect.relax_forbidden");
        return declared;
    }

    private static IEnumerable<AspectOverlay> EffectiveAspects(HarborlineOverlay overlay, string fieldName)
    {
        if (overlay.Aspects is not null) yield return overlay.Aspects;
        foreach (var section in overlay.Sections.Where(candidate => SectionContains(candidate, fieldName)))
        {
            if (section.Aspects is not null) yield return section.Aspects;
            foreach (var aspects in ItemAspects(section.Items, fieldName, [])) yield return aspects;
        }
        if (overlay.Fields[fieldName].Aspects is not null) yield return overlay.Fields[fieldName].Aspects!;
    }

    private static bool SectionContains(FormSection section, string fieldName) =>
        section.Fields.Contains(fieldName, StringComparer.Ordinal) || ItemContains(section.Items, fieldName);

    private static bool ItemContains(IReadOnlyList<FormItem>? items, string fieldName) =>
        items?.Any(item =>
            item.Kind == FormItemKind.Field && string.Equals(item.Key, fieldName, StringComparison.Ordinal) ||
            ItemContains(item.Items, fieldName)) == true;

    private static IEnumerable<AspectOverlay> ItemAspects(
        IReadOnlyList<FormItem>? items,
        string fieldName,
        IReadOnlyList<AspectOverlay> inherited)
    {
        foreach (var item in items ?? [])
        {
            var effective = item.Aspects is null ? inherited : inherited.Append(item.Aspects).ToArray();
            if (item.Kind == FormItemKind.Field && string.Equals(item.Key, fieldName, StringComparison.Ordinal))
                foreach (var aspects in effective) yield return aspects;
            foreach (var aspects in ItemAspects(item.Items, fieldName, effective)) yield return aspects;
        }
    }

    private static string TagKey(Tag tag) => $"{tag.System}\0{tag.Code}";

    private static FormDefinitionValidationException Fail(
        FormDefinition definition,
        string fieldName,
        string code) =>
        new(definition.Id, $"{code}: field '{fieldName}' failed publish policy admission.");
}
