using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class KernelPackageClosureTests
{
    private const string Platform = KernelPackageClosure.PlatformPackageKey;

    private static KernelPackageManifest Pack(string key, string version, params (string Key, string Pin)[] dependencies)
        => new(key, version, dependencies.Select(dependency => new KernelPackageDependency(dependency.Key, dependency.Pin)).ToArray());

    private static Dictionary<string, string> Active(params KernelPackageManifest[] packages)
        => packages.ToDictionary(package => package.Key, package => package.Version);

    private static string[] Keys(KernelPackageClosure closure) => closure.Packages.Select(package => package.Key).ToArray();

    private static KernelClosureRefusalException Refused(
        IEnumerable<string> roots, IEnumerable<KernelPackageManifest> packages, IReadOnlyDictionary<string, string> active)
        => Assert.Throws<KernelClosureRefusalException>(() => KernelPackageClosure.Resolve(roots, packages, active));

    private static void AssertRefusal(KernelClosureRefusalException refusal, string code, string[] path)
    {
        Assert.Equal(code, refusal.Code);
        Assert.Equal(path, refusal.Path);
    }

    [Fact]
    public void Closure_is_transitive_and_dependencies_precede_dependents()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0")), Pack("b", "1.0.0", ("c", "1.0.0")), Pack("c", "1.0.0"), Pack("unrelated", "1.0.0")];

        var closure = KernelPackageClosure.Resolve(["a"], packages, Active(packages));

        Assert.Equal([Platform, "c", "b", "a"], Keys(closure));
        Assert.Same(packages[3], closure.Packages[1]);
    }

    [Fact]
    public void A_transitive_missing_dependency_is_refused_with_its_path()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0")), Pack("b", "1.0.0", ("c", "1.0.0"))];

        var refusal = Refused(["a"], packages, Active(packages));

        Assert.Equal(KernelClosureErrors.DependencyMissing, refusal.Code);
        Assert.Equal(["a", "b", "c"], refusal.Path);
        Assert.Equal("kernel.closure.dependency-missing: a -> b -> c", refusal.Message);
    }

    [Fact]
    public void An_installed_but_inactive_dependency_is_refused()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0")), Pack("b", "1.0.0")];

        var refusal = Refused(["a"], packages, Active(packages[0], packages[1]));

        Assert.Equal(KernelClosureErrors.DependencyInactive, refusal.Code);
        Assert.Equal(["a", "b"], refusal.Path);
    }

    [Theory]
    [InlineData("1.2.2", "1.2.3")]
    [InlineData("1.1.9", "1.2.0")]
    [InlineData("0.9.9", "1.0.0")]
    [InlineData("1.2.3-rc.1", "1.2.3")]
    [InlineData("1.2.3-alpha", "1.2.3-beta")]
    [InlineData("1.2.3-alpha.2", "1.2.3-alpha.10")]
    [InlineData("1.2.3-1", "1.2.3-alpha")]
    [InlineData("1.2.3-alpha", "1.2.3-alpha.1")]
    [InlineData("9.2.3", "10.0.0")]
    [InlineData("1.2.3-10", "1.2.3-1-")]
    public void An_active_version_below_the_pin_is_refused(string activeVersion, string pin)
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", pin)), Pack("b", activeVersion)];

        var refusal = Refused(["a"], packages, Active(packages));

        Assert.Equal(KernelClosureErrors.DependencyBelowPin, refusal.Code);
        Assert.Equal(["a", "b"], refusal.Path);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.4", "1.2.3")]
    [InlineData("1.3.0", "1.2.9")]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("10.0.0", "9.2.3")]
    [InlineData("1.2.3", "1.2.3-rc.1")]
    [InlineData("1.2.3-beta", "1.2.3-alpha")]
    [InlineData("1.2.3-alpha.10", "1.2.3-alpha.2")]
    [InlineData("1.2.3-alpha", "1.2.3-1")]
    [InlineData("1.2.3-alpha.1", "1.2.3-alpha")]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("1.2.3+build.7", "1.2.3")]
    [InlineData("1.2.3-1-", "1.2.3-10")]
    public void A_pin_is_a_minimum_inclusive_floor_against_the_active_version(string activeVersion, string pin)
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", pin)), Pack("b", activeVersion)];

        Assert.Equal([Platform, "b", "a"], Keys(KernelPackageClosure.Resolve(["a"], packages, Active(packages))));
    }

    [Fact]
    public void Every_pin_on_an_already_resolved_dependency_is_still_checked()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0"), ("c", "1.0.0")),
            Pack("b", "1.0.0", ("d", "1.0.0")), Pack("c", "1.0.0", ("d", "2.0.0")), Pack("d", "1.5.0")];

        var refusal = Refused(["a"], packages, Active(packages));

        Assert.Equal(KernelClosureErrors.DependencyBelowPin, refusal.Code);
        Assert.Equal(["a", "c", "d"], refusal.Path);
    }

    [Fact]
    public void A_pin_on_the_platform_package_is_checked()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", (Platform, "2.0.0"))];

        var refusal = Refused(["a"], packages, Active(packages));

        Assert.Equal(KernelClosureErrors.DependencyBelowPin, refusal.Code);
        Assert.Equal(["a", Platform], refusal.Path);
    }

    [Fact]
    public void A_cycle_is_refused_naming_the_cycle_path()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0")), Pack("b", "1.0.0", ("c", "1.0.0")), Pack("c", "1.0.0", ("a", "1.0.0"))];

        var refusal = Refused(["a"], packages, Active(packages));

        Assert.Equal(KernelClosureErrors.Cycle, refusal.Code);
        Assert.Equal(["a", "b", "c", "a"], refusal.Path);
    }

    [Fact]
    public void A_self_dependency_is_a_cycle()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("a", "1.0.0"))];

        var refusal = Refused(["a"], packages, Active(packages));

        Assert.Equal(KernelClosureErrors.Cycle, refusal.Code);
        Assert.Equal(["a", "a"], refusal.Path);
    }

    [Fact]
    public void A_diamond_resolves_one_version_per_key()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0"), ("c", "1.0.0")),
            Pack("b", "1.0.0", ("d", "1.0.0")), Pack("c", "1.0.0", ("d", "1.1.0")), Pack("d", "1.1.0")];

        var closure = KernelPackageClosure.Resolve(["a"], packages, Active(packages));

        Assert.Equal([Platform, "d", "b", "c", "a"], Keys(closure));
    }

    [Fact]
    public void Two_supplied_versions_of_one_key_are_refused()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0")), Pack("b", "1.0.0"), Pack("b", "1.1.0")];

        var refusal = Refused(["a"], packages, Active(packages[0], packages[1], packages[3]));

        Assert.Equal(KernelClosureErrors.VersionConflict, refusal.Code);
        Assert.Equal(["b"], refusal.Path);
    }

    [Fact]
    public void A_manifest_that_is_not_the_active_version_is_refused()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0")), Pack("b", "1.0.0")];
        var active = Active(packages);
        active["b"] = "1.1.0";

        var refusal = Refused(["a"], packages, active);

        Assert.Equal(KernelClosureErrors.VersionConflict, refusal.Code);
        Assert.Equal(["a", "b"], refusal.Path);
    }

    [Fact]
    public void The_platform_package_is_the_implicit_first_root_of_every_closure()
    {
        KernelPackageManifest[] packages = [Pack("a", "1.0.0"), Pack(Platform, "1.0.0"), Pack("z", "1.0.0", ("a", "1.0.0"))];

        Assert.Equal([Platform, "a", "z"], Keys(KernelPackageClosure.Resolve(["z", "a"], packages, Active(packages))));
        Assert.Equal([Platform], Keys(KernelPackageClosure.Resolve([], packages, Active(packages))));
        Assert.Equal([Platform, "a"], Keys(KernelPackageClosure.Resolve([Platform, "a", Platform], packages, Active(packages))));
    }

    [Fact]
    public void A_missing_or_inactive_platform_package_refuses_every_closure()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0")];

        var missing = Refused(["a"], [packages[1]], Active(packages));
        var inactive = Refused(["a"], packages, Active(packages[1]));

        AssertRefusal(missing, KernelClosureErrors.DependencyMissing, [Platform]);
        AssertRefusal(inactive, KernelClosureErrors.DependencyInactive, [Platform]);
    }

    [Fact]
    public void Resolution_is_independent_of_input_order()
    {
        KernelPackageManifest[] packages = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("m", "1.0.0"), ("c", "1.0.0")),
            Pack("b", "1.0.0", ("c", "1.0.0")), Pack("c", "1.0.0"), Pack("m", "1.0.0")];
        var reversed = packages.Reverse().Select(package => package with { Dependencies = package.Dependencies.Reverse().ToArray() }).ToArray();

        var forward = Keys(KernelPackageClosure.Resolve(["a", "b"], packages, Active(packages)));
        var backward = Keys(KernelPackageClosure.Resolve(["b", "a"], reversed, Active(reversed)));

        Assert.Equal([Platform, "c", "m", "a", "b"], forward);
        Assert.Equal(forward, backward);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.x.0")]
    [InlineData("1a.0.0")]
    [InlineData("1.0.0+b_d.7")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-a..b")]
    [InlineData("1.0.0-a_b")]
    [InlineData("1.0.0+")]
    [InlineData("")]
    public void A_malformed_pin_or_active_version_is_refused(string version)
    {
        KernelPackageManifest[] badPin = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", version)), Pack("b", "1.0.0")];
        KernelPackageManifest[] badActive = [Pack(Platform, "1.0.0"), Pack("a", "1.0.0", ("b", "1.0.0")), Pack("b", version)];

        var pin = Refused(["a"], badPin, Active(badPin));
        var active = Refused(["a"], badActive, Active(badActive));

        AssertRefusal(pin, KernelClosureErrors.VersionInvalid, ["a", "b"]);
        AssertRefusal(active, KernelClosureErrors.VersionInvalid, ["a", "b"]);
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        // No platform package is supplied, so a missing guard would surface as a closure refusal instead.
        Assert.Throws<ArgumentNullException>(() => KernelPackageClosure.Resolve(null!, [], new Dictionary<string, string>()));
        Assert.Throws<ArgumentNullException>(() => KernelPackageClosure.Resolve([], null!, new Dictionary<string, string>()));
        Assert.Throws<ArgumentNullException>(() => KernelPackageClosure.Resolve([], [], null!));
    }
}
