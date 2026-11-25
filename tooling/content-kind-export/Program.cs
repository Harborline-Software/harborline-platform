using Harborline.Foundation.Definitions;
using Harborline.Tooling.ContentKinds;

var check = args.Contains("--check", StringComparer.Ordinal);
var options = args.Where(arg => arg != "--check").ToArray();
if (options.Length != 2 || options[0] != "--root")
{
    Console.Error.WriteLine("usage: content-kind-export --root <repository-root> [--check]");
    return 2;
}
var outputs = Generator.Generate(PackContentKindRegistry.Kinds, PackContentKindRegistry.Pillars, PackContentKindRegistry.Export(), Declarations.All);
var differences = Generator.Apply(Path.GetFullPath(options[1]), outputs, check);
foreach (var path in differences) Console.Error.WriteLine($"Generated file differs: {path}");
return differences.Count == 0 ? 0 : 1;
