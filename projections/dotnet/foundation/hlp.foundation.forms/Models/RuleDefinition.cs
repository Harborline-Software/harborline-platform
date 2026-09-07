namespace Harborline.Foundation.Forms.Models;

/// <summary>
/// One cross-field rule attached to a <see cref="FormDefinition"/> (ADR 0055
/// §"Three-tier rules", composing the cross-field-rules-engine UPF).
/// </summary>
/// <remarks>
/// <para>
/// The keystone substrate declares the rule shape; <b>evaluation lives in
/// the separate <c>Harborline.Foundation.RuleEngine</c> package</b> (forthcoming).
/// The keystone is intentionally inert with respect to rule semantics — it
/// stores rules as data so the form authoring UX, the entity-store write
/// path, the migration tooling, and the cross-device CRDT sync all see the
/// same canonical shape regardless of which evaluator runs.
/// </para>
/// <para>
/// <b>Expression contract:</b> the keystone treats <see cref="Expression"/>
/// as an opaque string. The evaluator interprets it according to
/// <see cref="Tier"/> — JSON Schema text, JsonLogic JSON, or Power Fx text.
/// Validation of the expression's grammar is the evaluator's responsibility;
/// the keystone only validates that the field is non-empty.
/// </para>
/// </remarks>
/// <param name="Id">Stable rule identifier, unique within the owning
/// <see cref="FormDefinition"/>. Recommended format <c>{scope}.{purpose}</c>
/// (for example <c>section.tenancy.read</c>). MUST be non-empty.</param>
/// <param name="Tier">Expression language tier.</param>
/// <param name="Scope">What the rule targets — a field, a section, or the
/// whole schema.</param>
/// <param name="ScopeTarget">For <see cref="RuleScope.Field"/> the field name;
/// for <see cref="RuleScope.Section"/> the section id; for
/// <see cref="RuleScope.Schema"/> the empty string. MUST be non-null;
/// MUST be non-empty when <see cref="Scope"/> is not <see cref="RuleScope.Schema"/>.</param>
/// <param name="Expression">Opaque expression text in the language indicated
/// by <see cref="Tier"/>. MUST be non-empty.</param>
/// <param name="Action">What the rule does when true.</param>
/// <param name="ErrorMessage">Optional localized message surfaced when the
/// rule fails (typically used with <see cref="RuleActionKind.Validate"/>).</param>
/// <param name="Presentation">Optional presentation payload carried by a
/// <see cref="RuleActionKind.Presentation"/> rule (SPINE-1 Decision DA). Additive +
/// back-compat: null on every pre-SPINE-1 rule.</param>
public sealed record RuleDefinition(
    string Id,
    RuleTier Tier,
    RuleScope Scope,
    string ScopeTarget,
    string Expression,
    RuleActionKind Action,
    InternationalizedText? ErrorMessage = null,
    PresentationHint? Presentation = null);
