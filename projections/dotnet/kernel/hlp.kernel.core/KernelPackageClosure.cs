namespace Harborline.Kernel.Core;

public static class KernelClosureErrors
{
    public const string DependencyMissing = "kernel.closure.dependency-missing";
    public const string DependencyInactive = "kernel.closure.dependency-inactive";
    public const string DependencyBelowPin = "kernel.closure.dependency-below-pin";
    public const string Cycle = "kernel.closure.cycle";
    public const string VersionConflict = "kernel.closure.version-conflict";
    public const string VersionInvalid = "kernel.closure.version-invalid";
}

/// <summary>A declared dependency; <paramref name="MinimumVersion"/> is a minimum-inclusive SemVer floor.</summary>
public sealed record KernelPackageDependency(string Key, string MinimumVersion);

/// <summary>One package version as its own manifest declares it.</summary>
public sealed record KernelPackageManifest(string Key, string Version, IReadOnlyList<KernelPackageDependency> Dependencies);

public sealed class KernelClosureRefusalException(string code, IReadOnlyList<string> path)
    : InvalidOperationException($"{code}: {string.Join(" -> ", path)}")
{
    public string Code { get; } = code;
    /// <summary>The dependency path from the closure root to the package that could not be satisfied.</summary>
    public IReadOnlyList<string> Path { get; } = path;
}

/// <summary>A resolved package closure in install order: the platform package first, every dependency before its dependents.</summary>
public sealed class KernelPackageClosure
{
    public const string PlatformPackageKey = "harborline.platform";

    private KernelPackageClosure(IReadOnlyList<KernelPackageManifest> packages) => Packages = packages;

    public IReadOnlyList<KernelPackageManifest> Packages { get; }

    /// <summary>
    /// Walks <paramref name="roots"/> transitively over the manifests in <paramref name="packages"/>, with the platform
    /// package as the implicit first root. Each key resolves to exactly one version, which must be its
    /// <paramref name="active"/> version and meet every pin on it; a cycle is refused.
    /// </summary>
    public static KernelPackageClosure Resolve(
        IEnumerable<string> roots,
        IEnumerable<KernelPackageManifest> packages,
        IReadOnlyDictionary<string, string> active)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(active);
        var byKey = new Dictionary<string, KernelPackageManifest>(StringComparer.Ordinal);
        foreach (var package in packages)
            if (!byKey.TryAdd(package.Key, package)) throw new KernelClosureRefusalException(KernelClosureErrors.VersionConflict, [package.Key]);

        var order = new List<KernelPackageManifest>();
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        Visit(PlatformPackageKey);
        foreach (var root in roots.Order(StringComparer.Ordinal)) Visit(root);
        return new KernelPackageClosure(order);

        void Visit(string key)
        {
            path.Add(key);
            if (path.IndexOf(key) != path.Count - 1) Refuse(KernelClosureErrors.Cycle);
            if (resolved.Add(key))
            {
                if (!byKey.TryGetValue(key, out var manifest)) Refuse(KernelClosureErrors.DependencyMissing);
                if (!active.TryGetValue(key, out var version)) Refuse(KernelClosureErrors.DependencyInactive);
                if (manifest!.Version != version) Refuse(KernelClosureErrors.VersionConflict);
                foreach (var dependency in manifest.Dependencies.OrderBy(dependency => dependency.Key, StringComparer.Ordinal))
                {
                    Visit(dependency.Key);
                    path.Add(dependency.Key);
                    if (Compare(Parse(active[dependency.Key]), Parse(dependency.MinimumVersion)) < 0)
                        Refuse(KernelClosureErrors.DependencyBelowPin);
                    path.RemoveAt(path.Count - 1);
                }
                order.Add(manifest);
            }
            path.RemoveAt(path.Count - 1);
        }

        void Refuse(string code) => throw new KernelClosureRefusalException(code, path.ToArray());

        SemanticVersion Parse(string value)
            => SemanticVersion.TryParse(value, out var parsed) ? parsed : throw new KernelClosureRefusalException(KernelClosureErrors.VersionInvalid, path.ToArray());
    }

    private sealed record SemanticVersion(string[] Core, string[] PreRelease)
    {
        public static bool TryParse(string value, out SemanticVersion parsed)
        {
            parsed = null!;
            var build = value.Split('+');
            var pieces = build[0].Split('-', 2);
            var core = pieces[0].Split('.');
            var preRelease = pieces.Length == 2 ? pieces[1].Split('.') : [];
            if (build.Length > 2 || (build.Length == 2 && !build[1].Split('.').All(Identifier))
                || core.Length != 3 || !core.All(part => Identifier(part) && Numeric(part) && NoLeadingZero(part))
                || !preRelease.All(part => Identifier(part) && (!Numeric(part) || NoLeadingZero(part))))
                return false;
            parsed = new(core, preRelease);
            return true;
        }

        private static bool Identifier(string part) => part.Length > 0 && part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
        private static bool Numeric(string part) => part.All(char.IsAsciiDigit);
        private static bool NoLeadingZero(string part) => part.Length == 1 || part[0] != '0';
    }

    /// <summary>SemVer 2.0.0 section 11 precedence; build metadata is ignored.</summary>
    private static int Compare(SemanticVersion left, SemanticVersion right)
    {
        for (var index = 0; index < 3; index++)
        {
            var compared = CompareIdentifier(left.Core[index], right.Core[index]);
            if (compared != 0) return compared;
        }
        if (left.PreRelease.Length == 0 || right.PreRelease.Length == 0)
            return right.PreRelease.Length.CompareTo(left.PreRelease.Length);
        for (var index = 0; index < Math.Min(left.PreRelease.Length, right.PreRelease.Length); index++)
        {
            var compared = CompareIdentifier(left.PreRelease[index], right.PreRelease[index]);
            if (compared != 0) return compared;
        }
        return left.PreRelease.Length.CompareTo(right.PreRelease.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = left.All(char.IsAsciiDigit), rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
        var compared = leftNumeric ? left.Length.CompareTo(right.Length) : 0;
        return compared != 0 ? compared : string.CompareOrdinal(left, right);
    }
}
