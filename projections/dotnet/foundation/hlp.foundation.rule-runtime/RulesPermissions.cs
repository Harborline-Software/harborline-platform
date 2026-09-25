namespace Harborline.Foundation.RuleEngine;

/// <summary>
/// The host's Access verdict for the acting principal on one capability name. Rules asks; it never decides, never
/// grants and holds no role system (DES-0018 §6). Role assignment of these names is a separate seed-policy decision.
/// </summary>
public delegate ValueTask<bool> RulesCapabilityCheck(string permission, CancellationToken cancellationToken);

/// <summary>
/// Closed capability names for the Rules operation boundary (DES-0018 §6, owner ruling 2026-09-25). They are broad
/// operation gates; the row, field and evidence-read checks behind them still apply.
/// </summary>
public static class RulesPermissions
{
    /// <summary>Author or change a draft rule.</summary>
    public const string Author = "rules:author";

    /// <summary>Publish a rule version, or materialise released Rules content.</summary>
    public const string Publish = "rules:publish";

    /// <summary>Author a sealed safety floor; strictly narrower than authoring an ordinary rule.</summary>
    public const string AuthorFloor = "rules:author-floor";

    /// <summary>Evaluate or explain a rule over records; always paired with <see cref="RecordsRead"/>.</summary>
    public const string EvaluateExplain = "rules:evaluate-explain";

    /// <summary>The Records read capability evaluate/explain must also hold; Rules never reads a record without it.</summary>
    public const string RecordsRead = "records:read";

    /// <summary>The refusal code for a capability the host did not grant.</summary>
    public const string DeniedCode = "rules.permission.denied";

    /// <summary>The four Rules capability names.</summary>
    public static IReadOnlyList<string> All { get; } = [Author, Publish, AuthorFloor, EvaluateExplain];

    /// <summary>True only when the host grants both <see cref="EvaluateExplain"/> and <see cref="RecordsRead"/>.</summary>
    public static async ValueTask<bool> AllowsEvaluateExplainAsync(RulesCapabilityCheck capabilities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return await capabilities(EvaluateExplain, cancellationToken).ConfigureAwait(false)
            && await capabilities(RecordsRead, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A Rules operation refused because the host did not grant its capability.</summary>
public sealed class RulesPermissionException(string permission)
    : Exception($"{RulesPermissions.DeniedCode}: {permission}")
{
    /// <summary>The stable refusal code.</summary>
    public string Code { get; } = RulesPermissions.DeniedCode;

    /// <summary>The capability that was not granted.</summary>
    public string Permission { get; } = permission;
}
