// Calculations capability vertical — the ENGINE lane. Restores ONLY the packed
// Harborline.Foundation.RuleAuthoring artifact (Harborline.Foundation.RuleEngine and
// Harborline.Contracts arrive transitively from the local feed) and drives the SAME 12-case
// authoring-verdict corpus through the packaged bridge, asserting the pinned expected
// verdicts AND cross-checking verdict identity against the renderer lane, case by case.
using System.Text.Json.Nodes;

using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Skins;

var corpus = JsonNode.Parse(File.ReadAllText("authoring-verdict-cases.json"))!.AsObject();
var cases = corpus["cases"]!.AsArray();
var clientVerdicts = JsonNode.Parse(File.ReadAllText("client-verdicts.json"))!.AsArray();

if (typeof(SkinLowering).Assembly.GetName().Name != "Harborline.Foundation.RuleAuthoring")
    throw new InvalidOperationException("Rule Authoring assembly identity changed.");

var verdicts = new JsonArray();
foreach (var row in cases.Select(node => node!.AsObject()))
{
    verdicts.Add(await VerdictOf(row, cases));
}

// Every verdict must match the pinned expected fields.
int expectedMismatches = 0;
for (int i = 0; i < cases.Count; i++)
{
    var expected = cases[i]!["expected"]!.AsObject();
    var verdict = verdicts[i]!.AsObject();
    foreach (var (key, value) in expected)
    {
        var actual = verdict.TryGetPropertyValue(key, out var v) ? v : null;
        if (!JsonNode.DeepEquals(actual, value)) expectedMismatches++;
    }
}
if (expectedMismatches > 0)
    throw new InvalidOperationException($"packed engine authoring bridge disagreed with the pinned corpus on {expectedMismatches} field(s): {verdicts.ToJsonString()}");
Console.WriteLine("CALCULATIONS_PACKAGE_PASS:" + new JsonObject { ["cases"] = cases.Count }.ToJsonString());

// Cross-lane identity: the engine lane's verdicts must equal the renderer lane's, case by case.
if (clientVerdicts.Count != verdicts.Count)
    throw new InvalidOperationException($"verdict counts diverge: client {clientVerdicts.Count} vs engine {verdicts.Count}");
int crossLaneMismatches = 0;
for (int i = 0; i < verdicts.Count; i++)
{
    if (!JsonNode.DeepEquals(verdicts[i], clientVerdicts[i])) crossLaneMismatches++;
}
if (crossLaneMismatches > 0)
    throw new InvalidOperationException($"cross-lane verdicts diverge on {crossLaneMismatches} case(s): engine={verdicts.ToJsonString()} client={clientVerdicts.ToJsonString()}");
Console.WriteLine("CALCULATIONS_CAPABILITY_PASS:" + new JsonObject
{
    ["cases"] = verdicts.Count,
    ["crossLaneMismatches"] = 0,
}.ToJsonString());

static async Task<JsonObject> VerdictOf(JsonObject row, JsonArray cases)
{
    string id = row["id"]!.GetValue<string>();
    string op = row["op"]!.GetValue<string>();
    switch (op)
    {
        case "preview":
        {
            var draft = DraftOf(row, cases);
            var sample = row["sample"]!.AsObject().DeepClone().AsObject();
            var result = SkinLowering.EvaluatePreview(draft, row["ruleId"]!.GetValue<string>(), sample);
            var traceCodes = new JsonArray();
            foreach (var code in result.Trace.Select(entry => entry.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal))
            {
                traceCodes.Add(code);
            }
            return new JsonObject
            {
                ["id"] = id,
                ["op"] = op,
                ["value"] = result.Value?.DeepClone(),
                ["firedRowId"] = draft is DecisionTableDraft ? result.FiredRowId : null,
                ["traceCodes"] = traceCodes,
            };
        }
        case "publish-refusal":
        {
            var draft = DraftOf(row, cases);
            string ruleKey = row["ruleKey"]!.GetValue<string>();
            var catalog = new RuleCatalog(new InMemoryRuleCatalogStore());
            await catalog.CreateRuleAsync(ruleKey, ruleKey, SkinTypeOf(draft), draft);
            // The fence's surface gate and the advisory lint must agree on the blank-Otherwise case.
            if (draft is DecisionTableDraft table && !RuleLint.NoMatchResolved(table)
                && RuleLint.LintTable(table).All(f => f.Code != RuleLintCodes.NoMatchUnresolved))
            {
                throw new InvalidOperationException($"lint and fence disagree on no-match for {id}");
            }
            var outcome = await PublishAdmission.PublishRuleAsync(catalog, ruleKey, draft);
            var stored = await catalog.LoadRuleAsync(ruleKey)
                ?? throw new InvalidOperationException($"rule vanished for {id}");
            if (stored.Versions.Count != 0)
                throw new InvalidOperationException($"refused publish still committed a version for {id}");
            return new JsonObject
            {
                ["id"] = id,
                ["op"] = op,
                ["ok"] = outcome.Ok,
                ["code"] = outcome.Ok ? null : outcome.Code,
            };
        }
        case "publish-mint":
        {
            var draft = DraftOf(row, cases);
            string ruleKey = row["ruleKey"]!.GetValue<string>();
            var catalog = new RuleCatalog(new InMemoryRuleCatalogStore());
            await catalog.CreateRuleAsync(ruleKey, ruleKey, SkinTypeOf(draft), draft);
            var first = await PublishAdmission.PublishRuleAsync(catalog, ruleKey, draft);
            await catalog.SaveDraftAsync(ruleKey, draft);
            var second = await PublishAdmission.PublishRuleAsync(catalog, ruleKey, draft);
            if (!first.Ok || !second.Ok)
                throw new InvalidOperationException($"monotonic mint publish failed for {id}");
            var stored = await catalog.LoadRuleAsync(ruleKey)
                ?? throw new InvalidOperationException($"rule vanished for {id}");
            var versions = new JsonArray();
            foreach (var version in stored.Versions) versions.Add(version.Version);
            return new JsonObject { ["id"] = id, ["op"] = op, ["versions"] = versions };
        }
        case "downgrade":
        {
            var draft = DraftOf(row, cases);
            string ruleKey = row["ruleKey"]!.GetValue<string>();
            var catalog = new RuleCatalog(new InMemoryRuleCatalogStore());
            await catalog.CreateRuleAsync(ruleKey, ruleKey, SkinTypeOf(draft), draft);
            foreach (var version in row["seedVersions"]!.AsArray())
            {
                await catalog.CommitPublishedVersionAsync(ruleKey, version!.GetValue<string>(), draft);
            }
            bool refused = false;
            try
            {
                await catalog.CommitPublishedVersionAsync(ruleKey, row["downgrade"]!.GetValue<string>(), draft);
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }
            var stored = await catalog.LoadRuleAsync(ruleKey)
                ?? throw new InvalidOperationException($"rule vanished for {id}");
            var versions = new JsonArray();
            foreach (var version in stored.Versions) versions.Add(version.Version);
            return new JsonObject
            {
                ["id"] = id,
                ["op"] = op,
                ["refused"] = refused,
                ["versions"] = versions,
                ["nextVersion"] = RuleCatalog.NextVersion(stored),
            };
        }
        case "compile-cycle":
        {
            var rules = row["rules"]!.AsArray().Select(node =>
            {
                var rule = node!.AsObject();
                var wire = new JsonObject
                {
                    ["id"] = rule["id"]!.GetValue<string>(),
                    ["tier"] = rule["tier"]!.GetValue<string>(),
                    ["scope"] = rule["scope"]!.GetValue<string>(),
                    ["scopeTarget"] = rule["scopeTarget"]!.GetValue<string>(),
                    ["expression"] = rule["expression"]!.ToJsonString(),
                    ["action"] = rule["action"]!.GetValue<string>(),
                };
                return FormsJson.Deserialize<RuleDefinition>(wire.ToJsonString());
            }).ToList();
            try
            {
                RuleCompiler.Compile(rules);
                return new JsonObject { ["id"] = id, ["op"] = op, ["code"] = null };
            }
            catch (RuleCompilationException error)
            {
                return new JsonObject { ["id"] = id, ["op"] = op, ["code"] = error.Code };
            }
        }
        default:
            throw new InvalidOperationException($"unknown corpus op: {op}");
    }
}

static RuleSkinType SkinTypeOf(RuleDraft draft)
    => draft is DecisionTableDraft ? RuleSkinType.Table : RuleSkinType.Formula;

static RuleDraft DraftOf(JsonObject row, JsonArray cases)
{
    JsonObject draft;
    if (row.TryGetPropertyValue("draft", out var direct) && direct is JsonObject directDraft)
    {
        draft = directDraft.DeepClone().AsObject();
    }
    else
    {
        string reference = row["draftRef"]!.GetValue<string>();
        var source = cases.Select(node => node!.AsObject())
            .First(c => c["id"]!.GetValue<string>() == reference);
        draft = source["draft"]!.AsObject().DeepClone().AsObject();
    }
    if (row.TryGetPropertyValue("draftOverrides", out var overrides) && overrides is JsonObject overrideObject)
    {
        foreach (var (key, value) in overrideObject) draft[key] = value?.DeepClone();
    }
    return ParseDraft(draft);
}

static RuleDraft ParseDraft(JsonObject draft)
{
    var scope = Enum.Parse<RuleScope>(draft["scope"]!.GetValue<string>());
    string scopeTarget = draft["scopeTarget"]!.GetValue<string>();
    var outputType = Enum.Parse<RuleActionKind>(draft["outputType"]!.GetValue<string>());
    if (draft["skin"]!.GetValue<string>() == "table")
    {
        var columns = draft["columns"]!.AsArray().Select(node =>
        {
            var column = node!.AsObject();
            return new ConditionColumn(
                column["id"]!.GetValue<string>(),
                column["input"]!.GetValue<string>(),
                ValueTypeOf(column["valueType"]!.GetValue<string>()));
        }).ToList();
        var rows = draft["rows"]!.AsArray().Select(node =>
        {
            var row = node!.AsObject();
            var cells = new Dictionary<string, TableCell>();
            foreach (var (columnId, cell) in row["cells"]!.AsObject())
            {
                cells[columnId] = ParseCell(cell!.AsObject());
            }
            return new TableRow(
                row["id"]!.GetValue<string>(),
                cells,
                row["output"]!.GetValue<string>(),
                row["priority"]!.GetValue<int>());
        }).ToList();
        var noMatchNode = draft["noMatch"]!.AsObject();
        NoMatchPosture noMatch = noMatchNode["kind"]!.GetValue<string>() == "default"
            ? new NoMatchPosture.Default(noMatchNode["value"]!.GetValue<string>())
            : new NoMatchPosture.CatchAll();
        return new DecisionTableDraft
        {
            Scope = scope,
            ScopeTarget = scopeTarget,
            OutputType = outputType,
            HitPolicy = draft["hitPolicy"]!.GetValue<string>() == "priority" ? HitPolicy.Priority : HitPolicy.FirstMatch,
            Columns = columns,
            Rows = rows,
            NoMatch = noMatch,
        };
    }
    var inputs = draft["inputs"]!.AsArray().Select(node =>
    {
        var input = node!.AsObject();
        return new FormulaInputDecl(
            input["id"]!.GetValue<string>(),
            input["ref"]!.GetValue<string>(),
            ValueTypeOf(input["type"]!.GetValue<string>()));
    }).ToList();
    var expressionNode = draft["expression"];
    return new FormulaDraft
    {
        Scope = scope,
        ScopeTarget = scopeTarget,
        OutputType = outputType,
        Inputs = inputs,
        Expression = expressionNode is null or not JsonObject ? null : ParseExpr(expressionNode.AsObject()),
    };
}

static TableCell ParseCell(JsonObject cell) => cell["kind"]!.GetValue<string>() switch
{
    "any" => new TableCell.Any(),
    "range" => new TableCell.Range(cell["lo"]!.GetValue<string>(), cell["hi"]!.GetValue<string>()),
    "compare" => new TableCell.Compare(cell["op"]!.GetValue<string>(), cell["value"]!.GetValue<string>()),
    var kind => throw new InvalidOperationException($"unknown cell kind: {kind}"),
};

static FormulaExpr ParseExpr(JsonObject expr) => expr["kind"]!.GetValue<string>() switch
{
    "ref" => new FormulaExpr.Ref(expr["ref"]!.GetValue<string>()),
    "literal" => new FormulaExpr.Literal(
        expr["value"]!.GetValue<string>(),
        ValueTypeOf(expr["valueType"]!.GetValue<string>())),
    "binary" => new FormulaExpr.Binary(
        expr["op"]!.GetValue<string>(),
        ParseExpr(expr["left"]!.AsObject()),
        ParseExpr(expr["right"]!.AsObject())),
    "if" => new FormulaExpr.If(
        ParseCondition(expr["when"]!.AsObject()),
        ParseExpr(expr["then"]!.AsObject()),
        ParseExpr(expr["else"]!.AsObject())),
    var kind => throw new InvalidOperationException($"unknown expression kind: {kind}"),
};

static FormulaCondition ParseCondition(JsonObject condition) => new(
    ParseExpr(condition["left"]!.AsObject()),
    condition["op"]!.GetValue<string>(),
    ParseExpr(condition["right"]!.AsObject()));

static ColumnValueType ValueTypeOf(string value) => value switch
{
    "number" => ColumnValueType.Number,
    "boolean" => ColumnValueType.Boolean,
    _ => ColumnValueType.Text,
};
