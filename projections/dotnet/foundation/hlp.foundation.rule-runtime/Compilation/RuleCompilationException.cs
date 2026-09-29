namespace Harborline.Foundation.RuleEngine.Compilation;

/// <summary>
/// Thrown when a definition is rejected at publish (SPINE-1 design §2.3, §4) —
/// a cycle, an exceeded static bound, an unparseable expression, or an
/// unsupported tier. Fail-closed: a rejected definition never reaches an instance.
/// </summary>
public sealed class RuleCompilationException : Exception
{
    /// <summary>Stable diagnostic code (<see cref="RuleEngineCodes"/> <c>rule.compile.*</c>).</summary>
    public string Code { get; }

    /// <summary>The offending rule id, when attributable to a single rule.</summary>
    public string? RuleId { get; }

    /// <summary>For a cycle rejection, the cycle path (cell keys + the rules forming it).</summary>
    public IReadOnlyList<string>? CyclePath { get; }

    /// <summary>Stable string params (for <c>rule.compile.work_exceeded</c>: <c>proof</c> and <c>ceiling</c>); empty otherwise.</summary>
    public IReadOnlyDictionary<string, string> Params { get; } = new Dictionary<string, string>();

    public RuleCompilationException(string code, string message, string? ruleId = null, IReadOnlyList<string>? cyclePath = null)
        : base(message)
    {
        Code = code;
        RuleId = ruleId;
        CyclePath = cyclePath;
    }

    /// <summary>A graph-level refusal (no single rule) carrying stable string params.</summary>
    public RuleCompilationException(string code, string message, IReadOnlyDictionary<string, string> @params)
        : this(code, message)
        => Params = new Dictionary<string, string>(@params);
}
