using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Skins;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>The outcome of a publish attempt. A failure carries the stable skin code the compiler
/// raised. Use the <see cref="Success"/>/<see cref="Failure"/> factories.</summary>
public sealed record PublishOutcome(
    bool Ok,
    string? Version,
    RuleDefinition? Definition,
    string? Code,
    string? Message)
{
    public static PublishOutcome Success(string version, RuleDefinition definition)
        => new(true, version, definition, null, null);

    public static PublishOutcome Failure(string code, string message)
        => new(false, null, null, code, message);
}

/// <summary>
/// The PUBLISH ADMISSION fence for named rules (ADR 0146 D7 — design §5.2). Publishing a rule is a
/// CONTROL change: callers NEVER commit a version directly — they route through this fence, which
/// (1) COMPILES the skin through the shipped compiler (so an F1 no-match / undeclared-ref
/// violation is a publish rejection carrying the stable <c>rule.skin.*</c> code), then (2) mints
/// the next version through the injected catalog (S-8 watermark; refuse-downgrade). The pinned
/// Harborline App fence ran this compile locally while awaiting node routes; at the Harborline seam the
/// SAME fence runs over the catalog port, so the authoritative admission and the persistence
/// commit share one transactional surface.
/// </summary>
public static class PublishAdmission
{
    /// <summary>The stable rejection code when the named rule does not exist in the catalog.</summary>
    public const string NotFoundCode = "rules.publish.not_found";

    /// <summary>
    /// Publishes a rule's current draft through the admission fence. Compiles the skin FIRST
    /// (fail-closed: any <c>rule.skin.*</c> rejection blocks publish and returns its code) and
    /// only then mints + commits the next version. Publish is a control change — callers gate this
    /// behind a human CP confirm (§5.2).
    /// </summary>
    public static async Task<PublishOutcome> PublishRuleAsync(RuleCatalog catalog, string ruleKey, RuleDraft draft)
    {
        // Surface-level F1 gate (design §2.3): a BLANK Otherwise default compiles (an empty string
        // is a legal else-value), so the skin compiler alone does not reject it — the fence must.
        // Block publish with the same stable code the compiler raises for the catch-all path.
        if (draft is DecisionTableDraft table && !RuleLint.NoMatchResolved(table))
        {
            return PublishOutcome.Failure(SkinCodes.NoMatchUnresolved, "no-match is unresolved");
        }

        RuleDefinition definition;
        try
        {
            // The admission compile fence — the SINGLE source of F1 / undeclared-ref rejection.
            definition = SkinLowering.CompileDraft(draft, ruleKey);
        }
        catch (RuleCompilationException e)
        {
            return PublishOutcome.Failure(e.Code, e.Message);
        }

        var rule = await catalog.LoadRuleAsync(ruleKey).ConfigureAwait(false);
        if (rule is null) return PublishOutcome.Failure(NotFoundCode, $"rule '{ruleKey}' not found");
        string version = RuleCatalog.NextVersion(rule);
        await catalog.CommitPublishedVersionAsync(ruleKey, version, draft).ConfigureAwait(false);
        return PublishOutcome.Success(version, definition);
    }

    /// <summary>
    /// Whether publishing this draft is a CONTROL change requiring a human CP confirm (design
    /// §5.2). Under D7 every rule publish is treated as a control change; kept as a method so a
    /// CP-reachability litmus is a drop-in.
    /// </summary>
    public static bool PublishIsControlChange(RuleDraft draft) => true;
}
