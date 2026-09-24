using Xunit;
namespace Harborline.Foundation.RuleAuthoring.Tests;
public sealed class RuleKeySuggestionTests
{
    [Fact]
    public void SuggestsSlugWithoutAllocatingOrAddingVersionSuffix()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "invoice-route", "invoice-route-2" };
        Assert.Equal("invoice-route-3", RuleKeySuggestion.Suggest("  Invoice Route!  ", taken));
        Assert.Equal("rule", RuleKeySuggestion.Suggest("!!!", taken));
        Assert.Equal(2, taken.Count);
    }
}
