using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.RuleEngine.References;

/// <summary>An exact pin: name, concrete version and digest. Nothing floats.</summary>
public sealed record ExactPin(string Name, string Version, string Digest);

/// <summary>A refused reference, by stable code.</summary>
public sealed class NamedReferenceException(string code, string message) : Exception(message)
{
    /// <summary>The stable refusal code.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// An immutable, Rules-owned named predicate (DES-0018 <c>rules-ck-22</c>): authored once, reused by
/// view filters, rule conditions, automation conditions and report populations, each of which stores
/// only its <see cref="Pin"/>. The expression is admitted by the compiler when the predicate is made.
/// </summary>
public sealed record NamedPredicate
{
    /// <summary>Creates the predicate; refuses a floating version or an expression the compiler rejects.</summary>
    public NamedPredicate(string name, string version, string expression)
    {
        Name = NamedReferences.Exact(name, version).Name;
        Version = version;
        Expression = NamedReferences.Canonical(expression);
        RuleCompiler.Compile([NamedReferences.Rule("predicate:" + name, Expression, RuleActionKind.Validate)]);
        Pin = new ExactPin(Name, Version, NamedReferences.Digest("predicate", Name, Version, Expression));
    }

    public string Name { get; }
    public string Version { get; }
    public string Expression { get; }
    public ExactPin Pin { get; }
}

/// <summary>
/// A named calculation (DES-0018 <c>rules-ck-23</c>, ADR 0033): a substrate under Rules, referenced by
/// exact pin and never inlined. <see cref="NamedReferences.Compute"/> is a pure call that returns its value.
/// </summary>
public sealed record NamedCalculation
{
    /// <summary>Creates the calculation; refuses a floating version or an expression the compiler rejects.</summary>
    public NamedCalculation(string name, string version, string expression)
    {
        Name = NamedReferences.Exact(name, version).Name;
        Version = version;
        Expression = NamedReferences.Canonical(expression);
        RuleCompiler.Compile([NamedReferences.Rule("calculation:" + name, Expression, RuleActionKind.Compute)]);
        Pin = new ExactPin(Name, Version, NamedReferences.Digest("calculation", Name, Version, Expression));
    }

    public string Name { get; }
    public string Version { get; }
    public string Expression { get; }
    public ExactPin Pin { get; }
}

/// <summary>
/// A cited legality rule set (DES-0018 <c>rules-ck-24</c>): sealed, jurisdiction-scoped, effective-dated
/// and package-owned. Rules carries the citation and its provenance; it has no body to evaluate and
/// nothing here interprets it.
/// </summary>
public sealed record LegalityRuleSetCitation
{
    public LegalityRuleSetCitation(string packageId, string name, string version, string digest, string jurisdiction,
        DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        NamedReferences.Exact(name, version);
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(jurisdiction)
            || digest is not { Length: 64 } || !digest.All(Uri.IsHexDigit) || effectiveTo < effectiveFrom)
            throw new NamedReferenceException(NamedReferences.Malformed, "a legality citation needs a package, jurisdiction, 64-hex digest and ordered dates");
        (PackageId, Pin, Jurisdiction, EffectiveFrom, EffectiveTo) = (packageId, new ExactPin(name, version, digest), jurisdiction, effectiveFrom, effectiveTo);
    }

    public string PackageId { get; }
    public ExactPin Pin { get; }
    public string Jurisdiction { get; }
    public DateOnly EffectiveFrom { get; }
    public DateOnly? EffectiveTo { get; }

    /// <summary>Canonical JSON preserving provenance, dates and jurisdiction.</summary>
    public string CanonicalJson => new JsonObject
    {
        ["digest"] = Pin.Digest,
        ["effectiveFrom"] = EffectiveFrom.ToString("O"),
        ["effectiveTo"] = EffectiveTo?.ToString("O"),
        ["jurisdiction"] = Jurisdiction,
        ["name"] = Pin.Name,
        ["packageId"] = PackageId,
        ["version"] = Pin.Version,
    }.ToJsonString();
}

/// <summary>What a named predicate is reused by (<c>rules-auth-9</c>).</summary>
public enum PredicateConsumer
{
    ViewFilter,
    RuleCondition,
    AutomationCondition,
    ReportPopulation,
    /// <summary>A Layout block's <c>show_when</c> guard (DES-0052 <c>layout-ck-29</c>, T-724 ruling 72).</summary>
    LayoutGuard,
}

/// <summary>
/// The immutable dependency closure a signed definition was published with. Consumers resolve their
/// pins here and only here, never against the live catalogue.
/// </summary>
public sealed class PinnedClosure
{
    private readonly Dictionary<(string, string), NamedPredicate> _predicates;
    private readonly Dictionary<(string, string), NamedCalculation> _calculations;

    public PinnedClosure(IEnumerable<NamedPredicate> predicates, IEnumerable<NamedCalculation> calculations)
    {
        _predicates = predicates.ToDictionary(p => (p.Name, p.Version));
        _calculations = calculations.ToDictionary(c => (c.Name, c.Version));
    }

    internal NamedPredicate Predicate(ExactPin pin) => Verify(pin, _predicates.GetValueOrDefault((pin.Name, pin.Version)), p => p.Pin);

    internal NamedCalculation Calculation(ExactPin pin) => Verify(pin, _calculations.GetValueOrDefault((pin.Name, pin.Version)), c => c.Pin);

    private static T Verify<T>(ExactPin pin, T? found, Func<T, ExactPin> pinOf) where T : class
    {
        NamedReferences.Exact(pin.Name, pin.Version);
        if (found is null) throw new NamedReferenceException(NamedReferences.Unresolved, $"{pin.Name}@{pin.Version} is not in the pinned closure");
        if (pinOf(found).Digest != pin.Digest) throw new NamedReferenceException(NamedReferences.DigestMismatch, $"{pin.Name}@{pin.Version} digest differs from the pin");
        return found;
    }
}

/// <summary>Resolution of named predicates and calculations through exact pins.</summary>
public static class NamedReferences
{
    public const string Floating = "rule.reference.floating";
    public const string Unresolved = "rule.reference.unresolved";
    public const string DigestMismatch = "rule.reference.digest_mismatch";
    public const string Malformed = "rule.reference.malformed";

    /// <summary>
    /// The guard a consumer evaluates for its pinned predicate. The same pin yields the same rule for every
    /// consumer kind; the consumer's own admission still governs evaluation.
    /// </summary>
    public static RuleDefinition Bind(PredicateConsumer consumer, ExactPin pin, PinnedClosure closure)
    {
        var predicate = closure.Predicate(pin);
        return Rule($"{consumer}:{pin.Name}@{pin.Version}", predicate.Expression, RuleActionKind.Validate);
    }

    /// <summary>
    /// <c>Compute</c> as a pure call (owner ruling 2026-09-21): resolves the exact calculation pin from the
    /// closure and returns its typed value over the caller's inert snapshot. It writes nothing and accepts
    /// no inline expression.
    /// </summary>
    public static ComputedValue Compute(ExactPin pin, PinnedClosure closure, IGuardEvaluator evaluator,
        RuleContextSnapshot context, EvaluationAdmission admission, CancellationToken ct = default)
    {
        var calculation = closure.Calculation(pin);
        return evaluator.EvaluateValue(Rule($"calculation:{pin.Name}@{pin.Version}", calculation.Expression, RuleActionKind.Compute),
            context, RuleEvalScope.Root, admission, ct);
    }

    internal static ExactPin Exact(string name, string version)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new NamedReferenceException(Malformed, "a named reference needs a name");
        if (string.IsNullOrWhiteSpace(version) || version is "latest" or "*" || version.Contains('*') || version.Contains('^') || version.Contains('~'))
            throw new NamedReferenceException(Floating, $"'{version}' is not an exact version");
        return new ExactPin(name, version, "");
    }

    internal static string Canonical(string expression)
    {
        try { return Sort(JsonNode.Parse(expression))?.ToJsonString() ?? "null"; }
        catch (JsonException) { throw new NamedReferenceException(Malformed, "the expression is not JSON"); }
    }

    internal static string Digest(string kind, string name, string version, string expression)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            new JsonObject { ["expression"] = JsonNode.Parse(expression), ["kind"] = kind, ["name"] = name, ["version"] = version }.ToJsonString())));

    internal static RuleDefinition Rule(string id, string expression, RuleActionKind action) => new()
    {
        Id = id, Tier = RuleTier.JsonLogic, Scope = RuleScope.Schema, ScopeTarget = "", Expression = expression, Action = action,
    };

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };
}
