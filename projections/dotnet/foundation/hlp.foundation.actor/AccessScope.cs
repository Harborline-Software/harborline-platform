using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;

namespace Harborline.Foundation.Authorization;

/// <summary>Record facts supplied by the tenant-bound record reader, never ambient state.</summary>
public sealed record AccessRecord(string Tenant, string Kind, string Id, IReadOnlyDictionary<string, JsonNode?> Fields);

/// <summary>The complete point-of-use request forwarded to the host's existing authorization gate.</summary>
public sealed record AccessRequest(string Operation, string Principal, string Tenant, AccessRecord Record, DateTimeOffset At);

/// <summary>Authority admitted by the host for exactly one scope evaluation and its declared references.</summary>
public sealed class AccessAuthorityContext(
    string principal, string tenant, string recordKind, string recordId, DateTimeOffset at,
    IEnumerable<string> references)
{
    internal string Principal { get; } = principal;
    internal string Tenant { get; } = tenant;
    internal string RecordKind { get; } = recordKind;
    internal string RecordId { get; } = recordId;
    internal DateTimeOffset At { get; } = at;
    internal ImmutableHashSet<string> References { get; } = references.ToImmutableHashSet(StringComparer.Ordinal);
}

/// <summary>A scope or row-check result with a stable public reason.</summary>
public sealed record AccessCheck(bool Allowed, string Reason);

/// <summary>
/// Admits all references before invoking the existing rule evaluator. This evaluates a grant's scope,
/// not an authorization verdict; only the host gate combines grants, standings and constraints.
/// </summary>
public sealed class AccessScopeEvaluator
{
    private readonly Func<DateTimeOffset, IGuardEvaluator> _evaluator;

    /// <summary>
    /// Uses the host's shared evaluator adapter at the predicate instant. The host owns the clock
    /// binding so this authorization contract cannot introduce a second production clock seam.
    /// </summary>
    public AccessScopeEvaluator(Func<DateTimeOffset, IGuardEvaluator> evaluator) =>
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

    /// <summary>Evaluates once under matching authority; undeclared reads never reach the evaluator.</summary>
    public AccessCheck Evaluate(string expression, AccessRequest request, AccessAuthorityContext? authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is null) return new(false, "access.authority_missing");
        if (string.IsNullOrWhiteSpace(request.Principal) || string.IsNullOrWhiteSpace(request.Tenant)
            || string.IsNullOrWhiteSpace(request.Record.Kind) || string.IsNullOrWhiteSpace(request.Record.Id)
            || request.Record.Tenant != request.Tenant || authority.Principal != request.Principal
            || authority.Tenant != request.Tenant || authority.RecordKind != request.Record.Kind
            || authority.RecordId != request.Record.Id || authority.At != request.At)
            return new(false, "access.authority_mismatch");

        try
        {
            var ast = JsonNode.Parse(expression);
            if (!ReferencesAdmitted(ast, authority.References)) return new(false, "access.reference_undeclared");
            var facts = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
            {
                ["principal"] = JsonValue.Create(request.Principal),
                ["tenant"] = JsonValue.Create(request.Tenant),
                ["instant"] = JsonValue.Create(request.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ["record.id"] = JsonValue.Create(request.Record.Id),
                ["record.kind"] = JsonValue.Create(request.Record.Kind),
            };
            foreach (var (name, value) in request.Record.Fields)
                if (authority.References.Contains($"record.{name}") && !facts.ContainsKey($"record.{name}"))
                    facts.Add($"record.{name}", value?.DeepClone());
            var result = _evaluator(request.At).EvaluateGuard(new RuleDefinition
            {
                Id = "access.scope_false", Tier = RuleTier.JsonLogic, Scope = RuleScope.Field,
                ScopeTarget = "access", Action = RuleActionKind.Validate, Expression = expression,
            }, RuleContextSnapshot.Capture(facts), RuleEvalScope.Root, AccessExpressionEnvironment.Admitted.For(EvaluationPhase.Run), cancellationToken);
            return new(result.Ok, result.Ok ? "access.scope_matched" : "access.scope_false");
        }
        catch (JsonException) { return new(false, "access.scope_invalid"); }
        catch (RuleCompilationException) { return new(false, "access.scope_invalid"); }
    }

    // Inspect every branch, including short-circuited branches, before any field can be read.
    private static bool ReferencesAdmitted(JsonNode? node, ImmutableHashSet<string> admitted)
    {
        if (node is JsonArray array) return array.All(child => ReferencesAdmitted(child, admitted));
        if (node is not JsonObject obj) return true;
        foreach (var (key, value) in obj)
        {
            if (key == "agg") return false;
            if (key == "var")
            {
                var path = value is JsonArray args ? args.FirstOrDefault() : value;
                if (path is not JsonValue scalar || !scalar.TryGetValue<string>(out var name)
                    || string.IsNullOrWhiteSpace(name) || !admitted.Contains(name)
                    || !(name is "principal" or "tenant" or "instant" || name.StartsWith("record.", StringComparison.Ordinal))) return false;
            }
            if (!ReferencesAdmitted(value, admitted)) return false;
        }
        return true;
    }
}
