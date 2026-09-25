using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Functions;

namespace Harborline.Foundation.RuleEngine.Environments;

/// <summary>The evaluation phases a borrower declaration marks applicable or inapplicable (ADR 0099 decision 8).</summary>
public enum EvaluationPhase
{
    AuthoringValidation,
    PublishValidation,
    Render,
    Submission,
    Run,
    SignOff,
}

/// <summary>
/// A borrowing member's typed expression environment (DES-0018 <c>rules-ck-28</c>, ADR 0099 decision 8).
/// The row lives in the borrower's own record; Rules only admits it. Every member is explicit: there is
/// no ambient default, and every phase is marked applicable or not.
/// </summary>
/// <param name="Borrower">The borrower's declaring item id, e.g. <c>forms-ck-13</c>.</param>
/// <param name="Grammar">Borrowed grammar and version, <c>harborline-jsonlogic/v1</c> in R1.</param>
/// <param name="Variables">Variable names and types; the scope tokens an expression may address are among them.</param>
/// <param name="Operations">Allowed functions and operators, by built-in key.</param>
/// <param name="Effects">Effect capability set; R1 admits only <c>field-read</c>.</param>
/// <param name="MissingValues">Null and missing-field semantics.</param>
/// <param name="TimeSource">The time source.</param>
/// <param name="TimeZone">The time-zone behaviour.</param>
/// <param name="Phases">Every phase, marked applicable (true) or inapplicable (false).</param>
/// <param name="Replay">The deterministic replay policy.</param>
public sealed record BorrowerEnvironmentDeclaration(
    string Borrower,
    string Grammar,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> Effects,
    string MissingValues,
    string TimeSource,
    string TimeZone,
    IReadOnlyDictionary<EvaluationPhase, bool> Phases,
    string Replay);

/// <summary>A refused admission or an evaluation outside the admitted environment, by stable code.</summary>
public sealed class BorrowerEnvironmentException(string code, string message) : Exception(message)
{
    /// <summary>The stable refusal code.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// A declaration Rules has admitted. It has no public constructor: the only way to hold one is
/// <see cref="BorrowerEnvironmentAdmission.Admit"/>, so an evaluation entry point that requires it
/// requires evidence of admission (the <c>ValidatedRecordBody</c> precedent).
/// </summary>
public sealed class AdmittedEnvironment
{
    private readonly HashSet<string> _operations;
    private readonly HashSet<string> _variables;

    internal AdmittedEnvironment(BorrowerEnvironmentDeclaration declaration)
    {
        Declaration = declaration;
        _operations = new HashSet<string>(declaration.Operations, StringComparer.Ordinal);
        _variables = new HashSet<string>(declaration.Variables.Keys, StringComparer.Ordinal);
    }

    /// <summary>The admitted declaration.</summary>
    public BorrowerEnvironmentDeclaration Declaration { get; }

    /// <summary>Evidence for one evaluation phase; refuses a phase the declaration marks inapplicable.</summary>
    public EvaluationAdmission For(EvaluationPhase phase) => Declaration.Phases.TryGetValue(phase, out var applicable) && applicable
        ? new EvaluationAdmission(this, phase)
        : throw new BorrowerEnvironmentException(BorrowerEnvironmentAdmission.PhaseNotAdmitted,
            $"{Declaration.Borrower} does not admit evaluation in phase {phase}");

    internal string? Refusal(IEnumerable<JsonNode?> programs)
    {
        foreach (var program in programs)
            if (Walk(program) is { } refusal) return refusal;
        return null;
    }

    private string? Walk(JsonNode? node)
    {
        if (node is not JsonObject { Count: 1 } call) return null;
        var (op, raw) = (call.First().Key, call.First().Value);
        if (!_operations.Contains(op)) return BorrowerEnvironmentAdmission.OperationNotAdmitted;
        var args = raw is JsonArray array ? array.ToList() : [raw];
        if (op == "var" && args.FirstOrDefault() is JsonValue path && path.TryGetValue<string>(out var p) && !Addresses(p))
            return BorrowerEnvironmentAdmission.VariableNotAdmitted;
        if (op == "agg" && !_variables.Contains("row")) return BorrowerEnvironmentAdmission.VariableNotAdmitted;
        foreach (var arg in args)
            if (Walk(arg) is { } refusal) return refusal;
        return null;
    }

    private bool Addresses(string path)
    {
        int dot = path.IndexOf('.');
        return _variables.Contains(dot < 0 ? "field" : path[..dot]);
    }
}

/// <summary>An admitted environment bound to the phase one evaluation runs in.</summary>
public sealed class EvaluationAdmission
{
    internal EvaluationAdmission(AdmittedEnvironment environment, EvaluationPhase phase)
    {
        Environment = environment;
        Phase = phase;
    }

    /// <summary>The admitted environment.</summary>
    public AdmittedEnvironment Environment { get; }

    /// <summary>The phase this evaluation runs in.</summary>
    public EvaluationPhase Phase { get; }
}

/// <summary>
/// Rules' admission of borrower environments (DES-0018 <c>rules-eng-26</c>, ADR 0095 rulings 10 and 11)
/// and the runtime check every evaluation entry point makes.
/// </summary>
public static class BorrowerEnvironmentAdmission
{
    /// <summary>The only grammar R1 lends.</summary>
    public const string Grammar = "harborline-jsonlogic/v1";

    /// <summary>The only effect capability R1 admits.</summary>
    public const string FieldRead = "field-read";

    /// <summary>The declaration is malformed or asks for something Rules does not lend.</summary>
    public const string DeclarationRefused = "rule.environment.declaration_refused";

    /// <summary>An evaluation arrived without admission evidence.</summary>
    public const string NotAdmitted = "rule.environment.not_admitted";

    /// <summary>The phase is not one the declaration marks applicable.</summary>
    public const string PhaseNotAdmitted = "rule.environment.phase_not_admitted";

    /// <summary>The program calls a function the declaration does not admit.</summary>
    public const string OperationNotAdmitted = "rule.environment.operation_not_admitted";

    /// <summary>The program addresses a variable the declaration does not admit.</summary>
    public const string VariableNotAdmitted = "rule.environment.variable_not_admitted";

    /// <summary>Admits a declaration or refuses it by <see cref="DeclarationRefused"/>.</summary>
    public static AdmittedEnvironment Admit(BorrowerEnvironmentDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        string? reason =
            Blank(declaration.Borrower) ? "borrower" :
            declaration.Grammar != Grammar ? "grammar" :
            declaration.Variables is null || declaration.Variables.Count == 0 || declaration.Variables.Any(v => Blank(v.Key) || Blank(v.Value)) ? "variables" :
            declaration.Operations is null || declaration.Operations.Any(op => !BuiltInFunctionRegister.TryResolve(op, out _)) ? "operations" :
            declaration.Effects is null || declaration.Effects.Any(effect => effect != FieldRead) ? "effects" :
            Blank(declaration.MissingValues) ? "missing-values" :
            Blank(declaration.TimeSource) || Blank(declaration.TimeZone) ? "time" :
            declaration.Phases is null || Enum.GetValues<EvaluationPhase>().Any(phase => !declaration.Phases.ContainsKey(phase)) ? "phases" :
            Blank(declaration.Replay) ? "replay" : null;
        return reason is null
            ? new AdmittedEnvironment(declaration)
            : throw new BorrowerEnvironmentException(DeclarationRefused, $"{declaration.Borrower}: {reason}");
    }

    /// <summary>
    /// The runtime check at an evaluation entry point: null when <paramref name="admission"/> admits every
    /// operation and variable the compiled program uses, otherwise the refusal code.
    /// </summary>
    internal static string? Check(EvaluationAdmission? admission, CompiledGraph compiled)
        => admission is null ? NotAdmitted : admission.Environment.Refusal(compiled.Rules.Select(rule => rule.Ast));

    /// <summary>Canonical JSON options shared with the TS tier's <c>JSON.stringify</c> (no HTML escaping).</summary>
    public static JsonSerializerOptions CanonicalOptions { get; } = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The canonical export of a declaration: sorted keys, every phase present with its applicability and
    /// every variable with its type, so scope narrowing and phase exclusions survive release byte for byte.
    /// </summary>
    public static string CanonicalJson(BorrowerEnvironmentDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return new JsonObject
        {
            ["borrower"] = declaration.Borrower,
            ["effects"] = new JsonArray([.. declaration.Effects.Select(effect => (JsonNode?)effect)]),
            ["grammar"] = declaration.Grammar,
            ["missingValues"] = declaration.MissingValues,
            ["operations"] = new JsonArray([.. declaration.Operations.Select(op => (JsonNode?)op)]),
            ["phases"] = new JsonObject(declaration.Phases.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal)
                .Select(p => KeyValuePair.Create(p.Key.ToString(), (JsonNode?)p.Value))),
            ["replay"] = declaration.Replay,
            ["timeSource"] = declaration.TimeSource,
            ["timeZone"] = declaration.TimeZone,
            ["variables"] = new JsonObject(declaration.Variables.OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => KeyValuePair.Create(v.Key, (JsonNode?)v.Value))),
        }.ToJsonString(CanonicalOptions);
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
