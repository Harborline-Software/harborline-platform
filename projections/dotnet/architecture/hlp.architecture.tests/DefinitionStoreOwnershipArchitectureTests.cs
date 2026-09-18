using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class DefinitionStoreOwnershipArchitectureTests
{
    private const string OwnerAssembly = "Harborline.Blocks.BuilderDefinitions";

    // T-620 explicitly leaves these three families on their current storage contracts.
    private static readonly HashSet<string> Retained = new(StringComparer.Ordinal)
    {
        "Harborline.Foundation.Forms.InMemoryFormDefinitionStore",
        "Harborline.Blocks.Workflow.Durable.InMemoryWorkflowDefinitionStore",
    };

    // These are NOT permissions to develop another store. T-620 lands before the member slices
    // that remove these pre-existing copies. Freeze the exact source until that removal; delete
    // the corresponding row when migrating the member. A new or changed copy fails the fence.
    private static readonly Dictionary<string, (string Path, string Hash, string Ticket)> RetiringUnchanged = new(StringComparer.Ordinal)
    {
        ["Harborline.Blocks.EntityViews.InMemoryViewDefinitionStore"] = (
            "projections/dotnet/blocks/hlp.blocks.entity-views/ViewDefinitionStore.cs",
            "07127313417a6f2120c3833dd9923dcaf8f94ba6a0548b7515caca0f5851cbe9", "T-486"),
        ["Harborline.Foundation.DataExchange.InMemoryDataExchangeDefinitionStore"] = (
            "projections/dotnet/foundation/hlp.foundation.data-exchange/DataExchangeDefinition.cs",
            "ca5abb6033488f2bbd8820a72b325fc789bd090043ba6836a2c84e94f2b93599", "T-600"),
        ["Harborline.Foundation.RuleAuthoring.InMemoryRuleCatalogStore"] = (
            "projections/dotnet/foundation/hlp.foundation.rule-authoring/RuleCatalog.cs",
            "baf33be5578d4e5e84b95303b7113f27a7d98326010b1ef86ce417bc658d7992", "T-588"),
    };

    [Fact]
    public void NewDefinitionStoresBelongOnlyToBuilderDefinitions()
    {
        var root = RepositoryRoot();
        var stores = Directory.EnumerateFiles(AppContext.BaseDirectory, "Harborline.*.dll")
            .Where(file => !Path.GetFileNameWithoutExtension(file).EndsWith(".Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsStore)
            .ToArray();
        Assert.NotEmpty(stores);
        var names = stores.Select(type => type.FullName!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Harborline.Blocks.BuilderDefinitions.InMemoryVersionedDefinitionStore", names);

        foreach (var store in stores)
        {
            if (IsOwnedOrRetained(store)) continue;
            Assert.True(RetiringUnchanged.TryGetValue(store.FullName!, out var frozen),
                $"New definition store outside builder-definitions: {store.FullName}. Bind to IVersionedDefinitionStore.");
            var source = File.ReadAllText(Path.Combine(root, frozen.Path)).Replace("\r\n", "\n", StringComparison.Ordinal);
            string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
            Assert.True(digest == frozen.Hash,
                $"Changed retiring store {store.FullName}. {frozen.Ticket} must remove the copy and its freeze row, not evolve it.");
        }
        foreach (var frozen in RetiringUnchanged.Keys)
            Assert.True(names.Contains(frozen), $"Remove stale retirement freeze: {frozen}.");
    }

    [Fact]
    public void FenceRejectsANewKernelStyleStoreEvenWhenItHasNoKnownInterface()
    {
        Assert.True(IsStore(typeof(RecordsDefinitionStore)));
        Assert.False(IsOwnedOrRetained(typeof(RecordsDefinitionStore)));
        Assert.False(RetiringUnchanged.ContainsKey(typeof(RecordsDefinitionStore).FullName!));
    }

    private static bool IsStore(Type type)
        => type.IsClass && !type.IsAbstract && (IsStoreName(type.Name)
            || type.GetInterfaces().Any(contract => IsStoreName(contract.Name)));

    private static bool IsStoreName(string name)
        => name.EndsWith("Store", StringComparison.Ordinal)
            && (name.Contains("Definition", StringComparison.Ordinal) || name.Contains("RuleCatalog", StringComparison.Ordinal));

    private static bool IsOwnedOrRetained(Type type)
        => type.Assembly.GetName().Name == OwnerAssembly || Retained.Contains(type.FullName!);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed class RecordsDefinitionStore { }
}
