using Xunit;
using Harborline.Foundation.Definitions;
using Harborline.Tooling.ContentKinds;

namespace Harborline.Tooling.ContentKinds.Tests;

public sealed class CommandTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) directory = directory.Parent!;
            return directory.FullName;
        }
    }

    private static (int Exit, string Output) Run(params string[] args)
    {
        // Invoke the executable's real entry point in-process so mutation coverage observes it too.
        var previousError = Console.Error;
        using var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Console.SetError(error);
            var entryPoint = typeof(Generator).Assembly.EntryPoint!;
            var exit = (int)entryPoint.Invoke(null, [args])!;
            return (exit, error.ToString());
        }
        finally
        {
            Console.SetError(previousError);
        }
    }

    [Fact(DisplayName = "T-738/platform-generated-pack-identities A2 malformed command returns usage without writing")]
    public void Invalid_arguments_are_refused()
    {
        Assert.Equal(2, Run().Exit);
        Assert.Equal(2, Run("--root").Exit);
        Assert.Equal(2, Run("--other", ".").Exit);
        Assert.Contains("usage:", Run().Output, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "T-738/platform-generated-pack-identities A2 checked-in files pass --check")]
    public void Checked_in_files_match()
    {
        Assert.Empty(Generator.Apply(Root, Generator.Generate(PackContentKindRegistry.Kinds, PackContentKindRegistry.Pillars, PackContentKindRegistry.Export(), Declarations.All), true));
        var result = Run("--root", Root, "--check");
        Assert.True(result.Exit == 0, result.Output);
    }

    [Theory(DisplayName = "T-738/platform-generated-pack-identities A1 A2 constant and export drift in both directions is read-only")]
    [InlineData("projections/dotnet/blocks/hlp.blocks.builder-definitions/LayoutPackIdentity.g.cs", "17", "16")]
    [InlineData("projections/dotnet/blocks/hlp.blocks.builder-definitions/LayoutPackIdentity.g.cs", "17", "18")]
    [InlineData("_shared/content-kinds/content-kinds.json", "\"schemaVersion\": 1", "\"schemaVersion\": 0")]
    [InlineData("_shared/content-kinds/content-kinds.json", "\"schemaVersion\": 1", "\"schemaVersion\": 2")]
    public void Drift_is_named_without_writes(string path, string before, string after)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "harborline", "t738-packids", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var write = Run("--root", scratch);
        Assert.True(write.Exit == 0, write.Output);
        var first = Directory.GetFiles(scratch, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
        Assert.Equal(10, first.Count);
        Assert.Equal(0, Run("--root", scratch).Exit);
        Assert.All(first, pair => Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)));
        var target = Path.Combine(scratch, path);
        var text = File.ReadAllText(target);
        Assert.Contains(before, text, StringComparison.Ordinal);
        File.WriteAllText(target, text.Replace(before, after, StringComparison.Ordinal));
        var snapshot = Directory.GetFiles(scratch, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
        var check = Run("--root", scratch, "--check");
        Assert.NotEqual(0, check.Exit);
        Assert.Contains(path, check.Output, StringComparison.Ordinal);
        Assert.All(snapshot, pair => Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)));
    }
}
