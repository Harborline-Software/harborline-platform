using System.Reflection;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Functions;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-590 slice 1: the discriminated function reference and the R1 built-in register.</summary>
public sealed class FunctionRegisterTests
{
    [Fact(DisplayName = "rules-ck-29, rules-bound-3: a package payload cannot construct the built-in discriminant")]
    public void Package_payload_cannot_construct_a_built_in()
    {
        // The type offers no public way in: only the register hands out BuiltInFunction.
        Assert.Empty(typeof(BuiltInFunction).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var forged = JsonNode.Parse("""{"kind":"builtin","key":"cat"}""");
        var refusal = Assert.Throws<FunctionReferenceException>(() => FunctionReferenceCodec.ReadPackagePayload(forged));
        Assert.Equal(FunctionReferenceCodec.BuiltInFromPackage, refusal.Code);

        // A package exporting a key that collides with a built-in is still the package arm: no shadowing.
        var package = FunctionReferenceCodec.ReadPackagePayload(JsonNode.Parse("""{"kind":"package","packageId":"finance","functionKey":"cat"}"""));
        var builtIn = BuiltInFunctionRegister.Resolve("cat");
        Assert.IsType<PackageFunction>(package);
        Assert.NotEqual<FunctionReference>(builtIn, package);
        Assert.NotEqual(builtIn.Digest, package.Digest);
    }

    [Fact(DisplayName = "rules-ck-29: canonical form and digest carry the discriminant and structured fields; the display form is never parsed")]
    public void Canonical_form_and_digest_carry_the_discriminant()
    {
        var builtIn = BuiltInFunctionRegister.Resolve("money.add");
        var package = new PackageFunction("finance", "money.add");
        Assert.Equal("""{"key":"money.add","kind":"builtin"}""", builtIn.CanonicalJson);
        Assert.Equal("""{"functionKey":"money.add","kind":"package","packageId":"finance"}""", package.CanonicalJson);
        Assert.Equal(64, builtIn.Digest.Length);
        Assert.NotEqual(builtIn.Digest, package.Digest);
        Assert.Equal("finance::money.add", package.Display);

        // ':' is forbidden in both structured fields, so the display form cannot be ambiguous.
        Assert.Equal(FunctionReferenceCodec.ColonForbidden, Assert.Throws<FunctionReferenceException>(() => new PackageFunction("fin:ance", "x")).Code);
        Assert.Equal(FunctionReferenceCodec.ColonForbidden, Assert.Throws<FunctionReferenceException>(() => new PackageFunction("finance", "a:b")).Code);

        // The display form is not a wire form: the codec refuses it instead of splitting it.
        Assert.Equal(FunctionReferenceCodec.MalformedReference,
            Assert.Throws<FunctionReferenceException>(() => FunctionReferenceCodec.Read(JsonValue.Create("finance::money.add"))).Code);

        // Round trip through the structured form keeps the arm.
        Assert.Same(builtIn, FunctionReferenceCodec.Read(JsonNode.Parse(builtIn.CanonicalJson)));
        Assert.Equal(package, FunctionReferenceCodec.Read(JsonNode.Parse(package.CanonicalJson)));
    }

    [Fact(DisplayName = "rules-eng-27: each built-in resolves exactly once from the register and every registered key executes")]
    public void Every_registered_built_in_executes()
    {
        var keys = BuiltInFunctionRegister.Functions.Select(function => function.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        // The register carries exactly the executable v1 built-ins: a deleted entry is a lost built-in.
        Assert.Equal(
            ["var", "missing", "missing_some", "==", "!=", "===", "!==", "!", "!!", "and", "or", "if", ">", ">=", "<", "<=",
             "+", "-", "*", "/", "%", "min", "max", "in", "cat", "agg", "money.add", "money.sub", "money.mul",
             "date.add", "date.diff", "date.today", "coding.is"],
            keys);
        Assert.All(BuiltInFunctionRegister.Functions, function => Assert.Same(function.Reference, BuiltInFunctionRegister.Resolve(function.Key)));

        foreach (var function in BuiltInFunctionRegister.Functions)
        {
            var args = new JsonArray(Enumerable.Range(0, function.MinArity).Select(_ => (JsonNode?)JsonValue.Create("1")).ToArray());
            var ctx = new EvalContext(new NullResolver(), DateTimeOffset.UnixEpoch, new EvalBudget(RuleEngineLimits.Default, CancellationToken.None));
            try
            {
                HarborlineJsonLogic.Evaluate(new JsonObject { [function.Key] = args }, ctx);
            }
            catch (RuleEvalException ex)
            {
                Assert.NotEqual(RuleEngineCodes.UnknownOperator, ex.Error.Code);
            }
        }
        Assert.False(BuiltInFunctionRegister.TryResolve("regex.match", out _));
    }

    [Fact(DisplayName = "rules-eng-27: the evaluator and the compiler read the register rather than a closed switch")]
    public void Evaluator_and_compiler_read_the_register()
    {
        var root = RepositoryRoot();
        var evaluator = File.ReadAllText(Path.Combine(root, "projections", "dotnet", "foundation", "hlp.foundation.rule-runtime", "Evaluation", "HarborlineJsonLogic.cs"));
        var compiler = File.ReadAllText(Path.Combine(root, "projections", "dotnet", "foundation", "hlp.foundation.rule-runtime", "Compilation", "RuleCompiler.cs"));
        Assert.DoesNotContain("return op switch", evaluator, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cat\" =>", evaluator, StringComparison.Ordinal);
        Assert.DoesNotContain("\"coding.is\"", compiler, StringComparison.Ordinal);

        // A key outside the register refuses at publish with the compiler's code.
        var unknown = new RuleDefinition { Id = "r", Tier = RuleTier.JsonLogic, Scope = RuleScope.Field, ScopeTarget = "a", Action = RuleActionKind.Compute, Expression = """{"shadow":[1]}""" };
        Assert.Equal(RuleEngineCodes.CompileInvalidExpression, Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile([unknown])).Code);
    }

    internal static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private sealed class NullResolver : IValueResolver
    {
        public RefValue ResolveVar(string path) => RefValue.Resolved(null);

        public RefValue ResolveAgg(string fn, string section, string col) => RefValue.Resolved(JsonValue.Create(0));
    }
}
