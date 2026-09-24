using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Skins;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>One compile-first admission path for authoring, publication and persisted source.</summary>
public static class RuleIntentValidator
{
    /// <summary>Validates exact body source reconstructed with shared metadata.</summary>
    public static RuleIntentResult ValidateBodyJson(string json, string id, string tenant, string version, RuleIntentPhase phase)
    {
        var parsed = RuleDefinitionCodec.ParseBody(json, id, tenant, version, phase);
        return parsed.IsValid ? ValidateParsed(parsed.Document!, phase) : parsed;
    }

    public static RuleIntentResult ValidateJson(string json, RuleIntentPhase phase)
    {
        var parsed = RuleDefinitionCodec.Parse(json, phase);
        return parsed.IsValid ? ValidateParsed(parsed.Document!, phase) : parsed;
    }

    public static RuleIntentResult Validate(RuleDefinitionDocument document, RuleIntentPhase phase)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            // Cross the same strict wire boundary as the other projection. The resulting source
            // is a detached snapshot, not a reference to the caller's mutable lists or JSON nodes.
            return ValidateJson(RuleDefinitionCodec.SerializeCanonical(document), phase);
        }
        catch (RuleDefinitionCodec.DefinitionReadException error)
        {
            return Refused(error.Code, error.Pointer, phase, document.Envelope.Id);
        }
    }

    private static RuleIntentResult ValidateParsed(RuleDefinitionDocument document, RuleIntentPhase phase)
    {
        if (document.Tier != RuleDefinitionTier.JsonLogic)
            return Refused(RuleEngineCodes.CompileUnsupportedTier, "/tier", phase, document.Envelope.Id);

        if (document.Draft is DecisionTableDraft table && !HasNoMatch(table))
            return Refused(SkinCodes.NoMatchUnresolved, "/draft/noMatch", phase, document.Envelope.Id);

        try
        {
            // Both authored skins lower to JsonLogic. JsonSchema remains the kernel validator's
            // distinct rule tier; it cannot relabel this source or bypass its compiler admission.
            var definition = SkinLowering.CompileDraft(document.Draft, document.Envelope.Id);
            var graph = RuleCompiler.Compile(new[] { definition });
            return new(document, Array.Empty<RuleIntentDiagnostic>()) { Lowered = graph.LoweredAsts.FirstOrDefault() };
        }
        catch (RuleCompilationException error)
        {
            string location = error.Code switch
            {
                SkinCodes.NoMatchUnresolved => "/draft/noMatch",
                SkinCodes.DecisionTableInvalidHitPolicy => "/draft/hitPolicy",
                SkinCodes.DecisionTableNoInputs => "/draft/columns",
                SkinCodes.DecisionTableEmpty or SkinCodes.DecisionTableBadCell or SkinCodes.DecisionTableBadRow => "/draft/rows",
                _ => document.Draft is FormulaDraft ? "/draft/expression" : "/draft",
            };
            return new(null, new[] { new RuleIntentDiagnostic(error.Code, location, phase,
                error.RuleId ?? document.Envelope.Id, error.CyclePath) });
        }
    }

    // A wildcard is unconditional wherever the declared hit policy places it. Lint may warn
    // about shadowed rows, but that warning cannot turn a resolvable table into a refusal.
    internal static bool HasNoMatch(DecisionTableDraft table)
        => table.NoMatch switch
        {
            NoMatchPosture.Default value => !string.IsNullOrWhiteSpace(value.Value),
            NoMatchPosture.CatchAll => table.Rows.Any(row => table.Columns.All(column =>
                !row.Cells.TryGetValue(column.Id, out var cell) || cell is TableCell.Any)),
            _ => false,
        };

    private static RuleIntentResult Refused(string code, string location, RuleIntentPhase phase, string ruleId)
        => new(null, new[] { new RuleIntentDiagnostic(code, location, phase, ruleId) });
}
