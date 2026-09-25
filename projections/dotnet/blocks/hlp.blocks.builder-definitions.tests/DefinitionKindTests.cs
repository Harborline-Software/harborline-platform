using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// The archive namespaces are a contract: two names on one value would merge two registries silently.
/// CA1069 is an error for accidental duplicates (.globalconfig) but allows an explicit alias; this gate refuses both.
/// </summary>
public sealed class DefinitionKindTests
{
    [Fact(DisplayName = "documents-ck-16: every DefinitionKind namespace, Templates included, has its own value; no member aliases another")]
    public void EveryNamespaceHasItsOwnValue()
    {
        var names = Enum.GetNames<DefinitionKind>();
        var values = Enum.GetValues<DefinitionKind>().Select(kind => (int)kind).Distinct().ToArray();

        Assert.Equal(names.Length, values.Length);
        Assert.Equal(12, (int)DefinitionKind.Templates);
        Assert.All(names, name => Assert.Equal(name, Enum.GetName(Enum.Parse<DefinitionKind>(name))));
    }
}
