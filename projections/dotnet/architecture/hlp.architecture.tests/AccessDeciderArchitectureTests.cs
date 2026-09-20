using System.Text.RegularExpressions;
using Xunit;

namespace Harborline.Architecture.Tests;

public sealed partial class AccessDeciderArchitectureTests
{
    [Fact]
    public void Consumers_cannot_implement_a_second_Access_verdict_calculator()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "repository.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        var offenders = Directory.EnumerateFiles(Path.Combine(root.FullName, "projections"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".tests", StringComparison.OrdinalIgnoreCase)
                && !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")
                && !path.Contains("hlp.foundation.actor", StringComparison.Ordinal))
            .Where(path => CalculatesVerdict(File.ReadAllText(path))).ToArray();
        Assert.True(offenders.Length == 0, "Access consumers must delegate to the existing gate: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("sealed class ViewDecider : IAuthorizationDecider { }")]
    [InlineData("return new AuthorizationDecisionEvidence(request, held.Any(r => required.Contains(r)), reason, binding, roles, standings);")]
    [InlineData("return held.Any(r => r.Atom.Covers(request.Act)) ? AuthorizationVerdict.Allowed : AuthorizationVerdict.Denied;")]
    [InlineData("sealed class ReportAuthority { bool Allow(dynamic atom, dynamic act) => atom.Covers(act); }")]
    [InlineData("sealed class ExportFilter { IAuthorizationClosureSnapshotReader reader; }")]
    public void Canary_rejects_a_planted_consumer_verdict_calculator(string consumer) => Assert.True(CalculatesVerdict(consumer));

    [Fact]
    public void Delegating_consumer_remains_permitted() => Assert.False(CalculatesVerdict(
        "var check = await access.CheckAsync(request, cancellationToken); return check.Allowed;"));

    private static bool CalculatesVerdict(string text) => SecondDecider().IsMatch(text);

    [GeneratedRegex(@"\b(?:class|record)\s+\w+[^{};]*:\s*[^{};]*\bIAuthorizationDecider\b|\bnew\s+(?:[\w.]+\.)?AuthorizationDecisionEvidence\s*\(|\bAuthorizationVerdict\s*\.\s*(?:Allowed|Denied)|\bIAuthorizationClosure(?:Snapshot)?Reader\b|\bAuthorizationEngine\b|\b(?:atom|Atom)\s*\.\s*Covers\s*\(")]
    private static partial Regex SecondDecider();
}
