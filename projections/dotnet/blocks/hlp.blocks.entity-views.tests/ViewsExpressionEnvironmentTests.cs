using Harborline.Blocks.EntityViews;
using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewsExpressionEnvironmentTests
{
    [Fact]
    public void Views_ck_14_declares_an_expression_environment()
    {
        Assert.NotNull(typeof(ViewDefinitionAdmission).Assembly.GetType("Harborline.Blocks.EntityViews.ViewsExpressionEnvironment"));
    }
}
