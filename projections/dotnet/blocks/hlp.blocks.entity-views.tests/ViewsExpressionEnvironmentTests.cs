using System.Text.Json.Nodes;
using Harborline.Blocks.EntityViews;
using Harborline.Foundation.RuleEngine.Environments;
using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewsExpressionEnvironmentTests
{
    [Fact]
    public void Views_ck_14_declaration_matches_the_released_borrower_contract()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "borrower-contracts.json")))!["borrowers"]!
            .AsArray().Single(entry => entry!["declaration"]?["borrower"]?.GetValue<string>() == "views-ck-14")!;
        Assert.Equal(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(ViewsExpressionEnvironment.Declaration));
        Assert.Same(ViewsExpressionEnvironment.Declaration, ViewsExpressionEnvironment.Admitted.Declaration);
        Assert.NotEqual(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(ViewsExpressionEnvironment.Declaration with { TimeSource = "other" }));
    }
}
