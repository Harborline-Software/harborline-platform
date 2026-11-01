using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Foundation.Forms.Engine.Authoring;

/// <summary>A host-attested bridge from a typed Form revision to its opaque publication identity.</summary>
/// <param name="Tenant">Exact owning tenant.</param>
/// <param name="DefinitionId">Stable Form identity.</param>
/// <param name="Version">Semantic label, never an opaque identity.</param>
/// <param name="VersionId">Opaque immutable publication identity supplied by the owning writer.</param>
/// <param name="Provenance">Immutable source/version evidence supplied by trusted composition.</param>
public sealed record FormPublicationBinding(TenantId Tenant, FormDefinitionId DefinitionId,
    SemanticVersion Version, string VersionId, string Provenance);

/// <summary>A pinned environment declaration, or an explicitly unknown historical environment.</summary>
/// <param name="Declaration">The recorded declaration; null means unknown.</param>
/// <param name="Provenance">The immutable declaration source, required for a known declaration.</param>
public sealed record FormPublicationEnvironment(BorrowerEnvironmentDeclaration? Declaration, string? Provenance);

/// <summary>Explicit publication facts. A failed predecessor lookup is never a first publication.</summary>
/// <param name="Candidate">Host-supplied binding for the proposed revision.</param>
/// <param name="Predecessor">Exact predecessor binding, absent only for explicit first publication.</param>
/// <param name="NoPredecessor">True only when the host declares a first publication.</param>
/// <param name="CandidateEnvironment">The candidate's recorded environment.</param>
/// <param name="PredecessorEnvironment">The predecessor's recorded environment, not today's default.</param>
public sealed record FormPublicationBasis(FormPublicationBinding Candidate, FormPublicationBinding? Predecessor,
    bool NoPredecessor, FormPublicationEnvironment CandidateEnvironment,
    FormPublicationEnvironment? PredecessorEnvironment);

/// <summary>Trusted composition inputs for compute-only publication assessment.</summary>
/// <param name="Basis">Explicit identities and historical declaration evidence.</param>
/// <param name="Schemas">The actual registry named by Form SchemaRef values.</param>
/// <param name="Surfaces">A complete-or-unavailable published-surface reader.</param>
public sealed record FormPublicationAssessmentContext(FormPublicationBasis Basis, ISchemaRegistry Schemas,
    IFormPublishedSurfaceSource Surfaces);

/// <summary>One immutable impacted surface version and its matching Form blocks.</summary>
/// <param name="Tenant">Owning tenant.</param>
/// <param name="DefinitionId">Stable surface identity.</param>
/// <param name="VersionId">Exact opaque surface version.</param>
/// <param name="Version">Semantic surface label.</param>
/// <param name="Digest">Observed immutable body digest.</param>
/// <param name="BlockIds">Ordinal, unique block identities pinned to the predecessor.</param>
public sealed record FormPublishedSurface(string Tenant, string DefinitionId, string VersionId,
    string Version, string Digest, IReadOnlyList<string> BlockIds);

/// <summary>A complete observed census or an honest inability to compute one.</summary>
public abstract record FormPublishedSurfaceRead
{
    /// <summary>A complete observed exact-tenant census, not a cross-store atomic snapshot.</summary>
    /// <param name="Tenant">Tenant actually observed by the adapter.</param>
    /// <param name="Surfaces">All impacted immutable surface versions.</param>
    public sealed record Complete(string Tenant, IReadOnlyList<FormPublishedSurface> Surfaces) : FormPublishedSurfaceRead;

    /// <summary>The adapter could not establish a complete census.</summary>
    /// <param name="Reason">A local diagnostic category, never exception text or a public refusal.</param>
    public sealed record Unavailable(string Reason) : FormPublishedSurfaceRead;
}

/// <summary>A read-only port; implementations must not publish, restore or rebind a surface.</summary>
public interface IFormPublishedSurfaceSource
{
    /// <summary>Reads all published surface versions pinned to the explicit predecessor.</summary>
    /// <param name="basis">Trusted exact publication identities.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    ValueTask<FormPublishedSurfaceRead> ReadAsync(FormPublicationBasis basis,
        CancellationToken cancellationToken = default);
}

/// <summary>The ADR 0099 decision 7 outcomes; Block is a compatibility result, not a new lifecycle refusal.</summary>
public enum FormPublicationCompatibility
{
    /// <summary>A human may explicitly republish the affected surfaces.</summary>
    /// <remarks>For explicit first publication: no predecessor, nothing to assess; the complete result has no proposal.</remarks>
    ProposalEligible,
    /// <summary>The author must resolve a changed contract.</summary>
    AuthorResolution,
    /// <summary>The proposed rebinding is incompatible.</summary>
    Block,
}

/// <summary>A proposal is data only; no component automatically applies it.</summary>
/// <param name="Predecessor">Original immutable Form binding.</param>
/// <param name="Candidate">Proposed immutable Form binding.</param>
/// <param name="Surfaces">Exact impacted surface versions, never latest-head references.</param>
public sealed record FormRepublishProposal(FormPublicationBinding Predecessor, FormPublicationBinding Candidate,
    IReadOnlyList<FormPublishedSurface> Surfaces);

/// <summary>The actual schema response used by an assessment, retained independently of the registry.</summary>
/// <param name="Id">The registry's returned schema identity.</param>
/// <param name="JsonSchemaText">The exact schema text parsed for comparison.</param>
public sealed record FormPublicationAssessedSchema(
    Harborline.Kernel.SchemaValidation.SchemaId Id, string JsonSchemaText);

/// <summary>Owned Form bodies and schema responses used by a historical assessment.</summary>
/// <remarks>Bindings and environments remain in Basis; exact Layout pins and digests remain in Surfaces.</remarks>
public sealed class FormPublicationAssessedInputs
{
    internal FormPublicationAssessedInputs(string candidateFormJson, string? predecessorFormJson,
        FormPublicationAssessedSchema candidateSchema, FormPublicationAssessedSchema? predecessorSchema)
    {
        CandidateFormJson = candidateFormJson;
        PredecessorFormJson = predecessorFormJson;
        CandidateSchema = candidateSchema;
        PredecessorSchema = predecessorSchema;
    }

    /// <summary>The entire candidate body captured before the first provider await, including its lifecycle metadata.</summary>
    public string CandidateFormJson { get; }

    /// <summary>The observed predecessor body; null only for an explicit first publication.</summary>
    public string? PredecessorFormJson { get; }

    /// <summary>The actual candidate schema identity and text.</summary>
    public FormPublicationAssessedSchema CandidateSchema { get; }

    /// <summary>The actual predecessor schema identity and text; null for an explicit first publication.</summary>
    public FormPublicationAssessedSchema? PredecessorSchema { get; }
}

/// <summary>Complete compatibility evidence, or Unavailable with no compatibility claim or proposal.</summary>
/// <remarks>
/// Explicit first publication means "no predecessor: nothing to assess". Its retained Complete/ProposalEligible
/// result has an empty impact set and no proposal. This convention adds no compatibility row to ADR 0099.
/// </remarks>
/// <param name="Basis">The supplied immutable binding/declaration evidence, if available.</param>
/// <param name="Compatibility">Null means unavailable.</param>
/// <param name="Surfaces">The complete impact set only when compatibility is known.</param>
/// <param name="Proposal">A compute-only proposal, only for an eligible known predecessor.</param>
/// <param name="UnavailableReason">Local inability-to-evaluate category; not a wire refusal code.</param>
public sealed record FormPublicationAssessment(FormPublicationBasis? Basis, FormPublicationCompatibility? Compatibility,
    IReadOnlyList<FormPublishedSurface> Surfaces, FormRepublishProposal? Proposal, string? UnavailableReason)
{
    /// <summary>Retained inputs for built-in complete assessments; null for unavailable or legacy results.</summary>
    public FormPublicationAssessedInputs? Inputs { get; internal init; }

    /// <summary>The freshness limit of the retained historical evidence; null when no inputs were retained.</summary>
    public string? FreshnessLimit => Inputs is null ? null :
        "Historical inputs observed before publication; the committed Form transition and current Layout membership are not guaranteed. Reassess before acting.";

    /// <summary>True only when a complete compatibility computation succeeded.</summary>
    public bool IsComplete => Compatibility is not null && UnavailableReason is null;

    /// <summary>Creates an explicitly unavailable assessment without a healthy/empty-set claim.</summary>
    /// <param name="reason">Local diagnostic category.</param>
    public static FormPublicationAssessment Unavailable(string reason) => new(null, null, [], null, reason);
}

/// <summary>The actual committed Form and the compute-only assessment made before its publication.</summary>
/// <param name="Definition">The revision returned by the actual atomic Form store publication.</param>
/// <param name="Assessment">Complete evidence or explicit unavailable; never an applied proposal.</param>
public sealed record FormDefinitionPublicationResult(FormDefinition Definition, FormPublicationAssessment Assessment);
