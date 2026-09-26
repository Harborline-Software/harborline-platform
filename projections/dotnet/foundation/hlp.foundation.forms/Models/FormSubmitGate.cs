using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;

namespace Harborline.Foundation.Forms.Models;

/// <summary>
/// DES-0016 forms-ck-4 — who may submit this form, declared on the form itself (L355, L356): exactly
/// one of a role, a record standing or an existing authorization capability (T-724 ruling 77). The
/// capability arm resolves only through Access's capability register, so a tenant-authored form can
/// never declare a capability of its own (forms-auth-16).
/// </summary>
/// <param name="Role">The role a submitter must hold.</param>
/// <param name="Standing">The standing a submitter must have.</param>
/// <param name="Capability">The authorization capability for the operation the form performs.</param>
public sealed record FormSubmitGate(
    RoleReference? Role = null,
    RecordStandingReference? Standing = null,
    AuthorizationCapabilityReference? Capability = null)
{
    /// <summary>Whether the gate names exactly one arm.</summary>
    [JsonIgnore]
    public bool IsWellFormed => (Role is null ? 0 : 1) + (Standing is null ? 0 : 1) + (Capability is null ? 0 : 1) == 1;
}
