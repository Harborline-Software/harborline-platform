using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>
/// The acting principal's submit answers for one capture-dominant surface on one record, at one instant.
/// The host builds it over its sole authorization decider; Layout asks and never computes a verdict.
/// </summary>
public interface ILayoutSubmitAccess
{
    /// <summary>Whether the principal satisfies the surface's submit gate: its role, standing or capability (layout-auth-23).</summary>
    /// <param name="gate">The admitted gate, holding exactly one arm.</param>
    bool Satisfies(LayoutSubmitGate gate);

    /// <summary>The existing Records write check on the record the surface captures into.</summary>
    bool CanWrite();
}

/// <summary>
/// DES-0052 C1 — the authority a returned surface carries. A surface is read-only unless the principal may
/// submit it, so the lanes render no live submit control on a read-only surface and derive nothing.
/// </summary>
/// <param name="CanSubmit">Whether the principal may submit the surface through the existing gate.</param>
public sealed record LayoutSurfaceAuthority(bool CanSubmit)
{
    /// <summary>Whether every block renders read-only.</summary>
    public bool ReadOnly => !CanSubmit;
}

/// <summary>Stable codes the open and submit gates refuse with, both at the <see cref="DefinitionAdmissionPhase.Render"/> stage (T-724 ruling 80).</summary>
public static class LayoutAuthorityCodes
{
    /// <summary>The principal may not open the surface (<c>layout:open</c>, layout-eng-26).</summary>
    public const string OpenForbidden = "layout.open.forbidden";

    /// <summary>The principal may not submit the surface: its submit gate or the Records write check refused (T-724 ruling 76).</summary>
    public const string SubmitForbidden = "layout.submit.forbidden";
}

/// <summary>
/// DES-0052 layout-eng-26 — the open and submit gates at the platform boundary, decided by kernel core.
/// Opening asks Access for <c>layout:open</c>. Submitting needs the surface's submit gate and the existing
/// Records write check, both; there is no <c>layout:submit</c> (T-724 ruling 76).
/// </summary>
public static class LayoutAuthorityGate
{
    /// <summary>Refuses a surface the reader may not open, before anything of it is read.</summary>
    /// <param name="surfaceId">The surface's definition identity.</param>
    /// <param name="reader">The reader's Access. Required: a missing port fails rather than opening.</param>
    public static void RequireOpen(string surfaceId, ILayoutAccess reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ArgumentNullException.ThrowIfNull(reader);
        if (!reader.CanOpen(surfaceId))
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Render, [new(LayoutAuthorityCodes.OpenForbidden, "")]);
    }

    /// <summary>The authority an opened surface carries for <paramref name="submitter"/>.</summary>
    /// <param name="definition">The admitted surface.</param>
    /// <param name="submitter">The principal's submit answers. Required: a missing port fails rather than submitting.</param>
    public static LayoutSurfaceAuthority Authority(LayoutDefinition definition, ILayoutSubmitAccess submitter)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(submitter);
        // Only a capture-dominant surface submits. Its gate, when it declares one, and the Records write
        // check must both allow; either alone never does.
        var canSubmit = definition.DefaultIntent == LayoutIntent.Capture
            && (definition.SubmitGate is not { } gate || submitter.Satisfies(gate))
            && submitter.CanWrite();
        return new(canSubmit);
    }

    /// <summary>Refuses a submit the principal may not make, asked afresh at the moment of submit.</summary>
    /// <param name="definition">The admitted surface.</param>
    /// <param name="submitter">The principal's submit answers.</param>
    public static void RequireSubmit(LayoutDefinition definition, ILayoutSubmitAccess submitter)
    {
        if (!Authority(definition, submitter).CanSubmit)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Render, [new(LayoutAuthorityCodes.SubmitForbidden, "")]);
    }
}
