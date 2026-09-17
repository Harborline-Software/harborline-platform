using Harborline.Blocks.BuilderDefinitions;

if (args.Length != 1)
    throw new ArgumentException("usage: platform-seed-export <output-path>");

await File.WriteAllBytesAsync(Path.GetFullPath(args[0]), PlatformPackageSeed.Export());
