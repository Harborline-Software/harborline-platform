using Harborline.Foundation.Definitions;

if (args.Length != 1)
    throw new ArgumentException("usage: content-kind-export <output-path>");

await File.WriteAllBytesAsync(Path.GetFullPath(args[0]), PackContentKindRegistry.Export());
