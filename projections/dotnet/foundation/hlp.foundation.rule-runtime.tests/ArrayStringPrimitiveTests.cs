using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

public sealed class ArrayStringPrimitiveTests
{
    private static RuleDefinition Rule(string expression) => RuleDefinitionFactory.Create(
        "quantifier", RuleTier.JsonLogic, RuleScope.Field, "result", expression, RuleActionKind.Compute);

    [Theory]
    [InlineData("some", true)]
    [InlineData("all", true)]
    [InlineData("none", false)]
    public void Local_paths_need_no_outer_variable_but_operations_still_require_admission(string op, bool expected)
    {
        var declaration = TestAdmission.Declaration(operations: [op, "var", "startsWith"], variables: ["clock"]);
        var admission = BorrowerEnvironmentAdmission.Admit(declaration).For(EvaluationPhase.Run);
        var guard = new GuardEvaluator(new FixedClock(DateTimeOffset.UnixEpoch));
        var context = RuleContextSnapshot.FromJsonText("""{"root":{}}""");
        var rule = Rule("{\"" + op + "\":" + """[[{"name":"pump"}],{"startsWith":[{"var":"name"},"p"]}]}""");
        var value = guard.EvaluateValue(rule, context, RuleEvalScope.Root, admission);
        Assert.Equal(ValueState.Resolved, value.State);
        Assert.Equal(expected, value.Value!.GetValue<bool>());

        var withoutText = BorrowerEnvironmentAdmission.Admit(declaration with { Operations = [op, "var"] }).For(EvaluationPhase.Run);
        Assert.Equal("rule.environment.operation_not_admitted", guard.EvaluateValue(rule, context, RuleEvalScope.Root, withoutText).Error?.Code);
        // An empty source cannot hide an unadmitted operation in the predicate.
        Assert.Equal("rule.environment.operation_not_admitted", guard.EvaluateValue(
            Rule("{\"" + op + "\":" + """[[],{"startsWith":["pump","p"]}]}"""), context, RuleEvalScope.Root, withoutText).Error?.Code);
        Assert.Equal("rule.environment.variable_not_admitted", guard.EvaluateValue(
            Rule("{\"" + op + "\":" + """[{"var":"values"},true]}"""), context, RuleEvalScope.Root, admission).Error?.Code);
    }

    [Theory]
    [InlineData("some")]
    [InlineData("all")]
    [InlineData("none")]
    [InlineData("startsWith")]
    [InlineData("endsWith")]
    public void Each_new_operation_requires_exactly_two_arguments(string op)
    {
        foreach (var arguments in new[] { "[]", "[[]]", "[[],true,false]" })
        {
            var exception = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile([Rule("{\"" + op + "\":" + arguments + "}")]));
            Assert.Equal("rule.compile.invalid_expression", exception.Code);
        }
    }

    [Theory]
    [InlineData("""{"all":[[1],true]}""", "60082")]
    [InlineData("""{"all":[[true],{"==":[{"var":""},true]}]}""", "5243060026")]
    [InlineData("""{"all":[[[1]],{"all":[[1],true]}]}""", "300370086")]
    [InlineData("""{"startsWith":["abc","a"]}""", "130")]
    [InlineData("""{"endsWith":["abc","a"]}""", "130")]
    public void Work_proof_accounts_for_each_iteration_and_string_scan(string expression, string work)
    {
        // Independent arithmetic: [1] costs 35, true costs 5; 5000 iterations cost
        // 1+35+5000*(1+5)=30036, then two Compute executions of work+5=60082.
        // A local var has work 262151/result 262144; equality with true has work
        // 1+262151+5+262144+4=524305. [true] costs 7, so the graph costs
        // 2*(1+7+5000*(1+524305)+5)=5243060026.
        // Nesting repeats the inner transfer; "abc"/"a" have 20/8 proof bytes.
        var proof = RuleCompiler.Compile([Rule(expression)]).WorkProof;
        Assert.Equal(work, proof.MaximumEvaluationWork.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("16394000", proof.MaximumResultBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var ceiling = System.Numerics.BigInteger.Parse(work, System.Globalization.CultureInfo.InvariantCulture);
        _ = RuleCompiler.Compile([Rule(expression)], RuleEngineLimits.Default with { MaxStaticWork = ceiling });
        var refusal = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile([Rule(expression)],
            RuleEngineLimits.Default with { MaxStaticWork = ceiling - 1 }));
        Assert.Equal("rule.compile.work_exceeded", refusal.Code);
    }

    [Fact]
    public void Local_paths_do_not_inherit_a_graph_producers_type()
    {
        var result = CoreTypeDerivation.Derive(JsonNode.Parse("""{"all":[[[1]],{"+":[{"var":"field.result"},1]}]}"""),
            "local", _ => CoreJsonType.Object);
        Assert.Equal(CoreJsonType.Boolean, result.Types);
        Assert.True(result.CanError);
    }
}
