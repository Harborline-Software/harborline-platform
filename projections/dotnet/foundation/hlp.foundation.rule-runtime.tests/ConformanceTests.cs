using System.Text;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Conformance;
using Harborline.Foundation.RuleEngine.Graph;


using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// The dual-tier conformance harness (SPINE-1 design §6.1, §6.4). Loads the
/// shared, language-neutral JSON corpus — the SAME files the <c>@harborline-software/rule-engine</c>
/// TS suite loads — runs each case through the .NET engine, and asserts the
/// lowered AST + the per-rule outcomes match byte-for-byte (canonical JSON). A
/// divergence here OR in the TS suite fails CI; both asserting against the one
/// corpus is what proves the two evaluators agree.
/// </summary>
/// <remarks>
/// <para><b>Exact sets, not lower bounds — ticket 193 review, 2026-08-31.</b> Until this change every
/// declared <c>expectedOutcomes</c> / <c>expectedAst</c> / <c>expectedCompiledExpression</c> key was
/// compared, but nothing asserted the produced key set EQUALED the declared one. A rule-engine
/// regression that emitted an unintended EXTRA outcome while preserving every declared expectation
/// kept this gate green — on both tiers, and in the cross-tier artifact diff
/// (<c>conformance/hlp.foundation.rule-runtime/runners/run-shared.mjs</c>), which compared only the
/// declared keys too. Both key sets are now asserted whole, and the artifact emits the PRODUCED keys.</para>
///
/// <para><b>Positive control.</b> Dropping <c>rc.line#r2</c> from <c>child-table.json</c>'s
/// <c>table/per-row-compute</c> — leaving the engine producing an outcome the case does not declare —
/// is GREEN on the pre-change runners and RED on these, on the .NET and TS tiers alike. Injecting a
/// synthesized extra rule into <c>RuleCompiler.Compile</c> trips the compiled-set assertion in 75 of
/// 79 cases. Both controls were reverted; no corpus byte changed in this commit.</para>
///
/// <para><b>Result: no divergence surfaced.</b> All 78 cases stayed green under the tightened
/// assertions. Recorded because "nothing broke" is the finding here: the corpus already declares
/// every outcome key both engines produce, including the per-row <c>id#rowId</c> expansions, so the
/// gap was latent coverage rather than a live disagreement. Nothing was loosened to get green.</para>
/// </remarks>
public sealed class ConformanceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (file, caseObj) in CorpusLoader.AllCases())
        {
            yield return new object[] { file, caseObj["name"]!.GetValue<string>(), caseObj };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Corpus_case_matches_byte_identical(string file, string name, JsonObject caseObj)
    {
        _ = file;
        _ = name;
        // Ticket 162: a case may pin a publish-time refusal or a guard-tier evaluation instead
        // of form-graph outcomes.
        if (caseObj.ContainsKey("expectedCompileError"))
        {
            Assert.Equal(caseObj["expectedCompileError"]!["code"]!.GetValue<string>(), CorpusLoader.CompileRefusalCode(caseObj));
            return;
        }
        if (caseObj.ContainsKey("guardValue"))
        {
            Assert.Equal(Canonical(caseObj["expectedGuardValue"]), CorpusLoader.GuardValueOutcome(caseObj));
            return;
        }
        var (compiled, result) = CorpusLoader.RunCase(caseObj);

        // 0. Pin the SKIN compiler output (expectedCompiledExpression) — the pre-lowering expression a
        //    decision-table / formula skin produces (ADR 0146 D2). Asserting it byte-identical proves both
        //    tiers' skin compilers agree on the hit-policy lowering (the `if` cascade + reified bounds).
        if (caseObj.TryGetPropertyValue("expectedCompiledExpression", out var ceNode) && ceNode is JsonObject expectedCompiled)
        {
            foreach (var (ruleId, expected) in expectedCompiled)
            {
                var rule = compiled.Rules.FirstOrDefault(r => r.Source.Id == ruleId);
                Assert.True(rule is not null, $"[{name}] expectedCompiledExpression names unknown rule '{ruleId}'");
                var produced = JsonNode.Parse(rule!.Source.Expression);
                Assert.Equal(Canonical(expected), Canonical(produced));
            }
        }

        // 0b. EXACT compiled set, not a lower bound (ticket 193 review): expectedCompiledExpression /
        //     expectedAst above name only the rules a case cares to pin, so neither notices a rule the
        //     compiler INVENTED. Every compiled rule must trace back to a rule the case declared, once.
        //     (Subset, not equality: RuleCompiler deliberately drops Tier-1 JsonSchema rules — the kernel
        //     validator owns those — so a declared rule legitimately need not compile.)
        var declaredIds = CorpusLoader.DeclaredRules(caseObj).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var compiledIds = compiled.Rules.Select(r => r.Source.Id).ToList();
        var undeclared = compiledIds.Where(id => !declaredIds.Contains(id)).ToList();
        Assert.True(undeclared.Count == 0,
            $"[{name}] the compiled graph carries rule(s) the case never declared: {string.Join(", ", undeclared)}");
        Assert.True(compiledIds.Count == compiledIds.Distinct(StringComparer.Ordinal).Count(),
            $"[{name}] a declared rule compiled more than once: {string.Join(", ", compiledIds)}");

        // 1. Pin the lowering (expectedAst), where the case declares it.
        if (caseObj.TryGetPropertyValue("expectedAst", out var astNode) && astNode is JsonObject expectedAst)
        {
            foreach (var (ruleId, expected) in expectedAst)
            {
                var rule = compiled.Rules.FirstOrDefault(r => r.Source.Id == ruleId);
                Assert.True(rule is not null, $"[{name}] expectedAst names unknown rule '{ruleId}'");
                Assert.Equal(Canonical(expected), Canonical(rule!.Ast));
            }
        }

        // 2. Pin the per-rule outcomes (expectedOutcomes), byte-identical — as an EXACT set, not a
        //    lower bound (ticket 193 review). The key set is asserted whole BEFORE the values, so a
        //    regression that emits an unintended extra outcome while satisfying every declared
        //    expectation is a red case rather than a silent pass.
        var expectedOutcomes = (JsonObject)caseObj["expectedOutcomes"]!;
        Assert.Equal(
            expectedOutcomes.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList(),
            result.ByRule.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        foreach (var (ruleKey, expected) in expectedOutcomes)
        {
            string actual = CanonicalJson.SerializeOutcome(result.ByRule[ruleKey]);
            Assert.Equal(Canonical(expected), actual);
        }
    }

    [Fact]
    public void Emit_cross_tier_artifact()
    {
        var artifact = new JsonObject();
        foreach (var (_, caseObj) in CorpusLoader.AllCases())
        {
            string name = caseObj["name"]!.GetValue<string>();
            if (caseObj.ContainsKey("expectedCompileError"))
            {
                artifact[name] = new JsonObject { ["(compile)"] = CorpusLoader.CompileRefusalCode(caseObj) };
                continue;
            }
            if (caseObj.ContainsKey("guardValue"))
            {
                artifact[name] = new JsonObject { [caseObj["guardValue"]!.GetValue<string>()] = CorpusLoader.GuardValueOutcome(caseObj) };
                continue;
            }
            var (_, result) = CorpusLoader.RunCase(caseObj);
            // Ticket 193 review: iterate the PRODUCED keys, not the declared ones. Emitting only
            // `expectedOutcomes` made the cross-tier artifact diff (runners/run-shared.mjs) a lower
            // bound as well — an extra outcome on BOTH tiers was invisible to it twice over.
            var perCase = new JsonObject();
            foreach (var ruleKey in result.ByRule.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                perCase[ruleKey] = CanonicalJson.SerializeOutcome(result.ByRule[ruleKey]);
            }
            artifact[name] = perCase;
        }
        CorpusLoader.WriteArtifact("dotnet-outcomes.json", artifact);
    }

    private static string Canonical(JsonNode? node)
    {
        var sb = new StringBuilder();
        CanonicalJson.Write(node, sb);
        return sb.ToString();
    }
}
