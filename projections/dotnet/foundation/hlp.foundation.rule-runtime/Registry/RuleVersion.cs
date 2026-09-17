using System.Globalization;

namespace Harborline.Foundation.RuleEngine.Registry;

/// <summary>
/// The per-rule-key monotonic-version comparator (ADR 0146 D5). Reuses the shipped S-8
/// monotonic-watermark SEMANTICS of <c>foundation-packs</c>' <c>PackVersion</c> — parse
/// <c>major.minor.patch</c> (extra numeric segments compared in order; a pre-release suffix after
/// '-' orders BEFORE the same core per SemVer §11; an unparseable version is refused) — as the
/// "which rule version wins offline" order. This is NOT a new versioning
/// primitive (D5: compose shipped patterns, no new primitive); it is the pack watermark applied
/// per rule key. It is mirrored here rather than referenced because the kernel rule engine cannot
/// depend UP on the pack machinery (packs consume rules as <c>PackContentKind.RuleDefinition</c>,
/// D8) — kept byte-compatible with <c>PackVersion.Compare</c>/<c>IsDowngrade</c>.
///
/// <para><c>foundation-versioning.VersionVector</c> is deliberately NOT used — it is a federation
/// handshake check, not a last-writer-wins watermark (a false friend, per the substrate survey).</para>
/// </summary>
public static class RuleVersion
{
    private sealed record ParsedVersion(int[] Core, string PreRelease, string BuildMetadata);

    /// <summary>
    /// Compares two rule versions. Returns &lt;0 if <paramref name="a"/> precedes
    /// <paramref name="b"/>, 0 if equal, &gt;0 if <paramref name="a"/> follows <paramref name="b"/>.
    /// A total, deterministic order — so two offline peers holding the same published set resolve
    /// the identical "latest" regardless of the order records synced in (monotonically convergent).
    /// </summary>
    public static int Compare(string a, string b)
    {
        var parsedA = Parse(a);
        var parsedB = Parse(b);
        var coreA = parsedA.Core;
        var coreB = parsedB.Core;
        var preA = parsedA.PreRelease;
        var preB = parsedB.PreRelease;

        var max = System.Math.Max(coreA.Length, coreB.Length);
        for (var i = 0; i < max; i++)
        {
            var ai = i < coreA.Length ? coreA[i] : 0;
            var bi = i < coreB.Length ? coreB[i] : 0;
            if (ai != bi)
            {
                return ai.CompareTo(bi);
            }
        }

        // Equal core: a pre-release precedes the stable release of the same core (SemVer §11).
        var hasPreA = preA.Length > 0;
        var hasPreB = preB.Length > 0;
        if (hasPreA == hasPreB)
        {
            return string.CompareOrdinal(preA, preB);
        }

        return hasPreA ? -1 : 1;
    }

    /// <summary>True iff <paramref name="candidate"/> is strictly below <paramref name="watermark"/>
    /// (a downgrade, refused by default under S-8 — the reject-stale half of the watermark).</summary>
    public static bool IsDowngrade(string watermark, string candidate)
        => Compare(candidate, watermark) < 0;

    /// <summary>Refuses a malformed rule version.</summary>
    public static void Validate(string version) => _ = Parse(version);

    /// <summary>Returns the next patch version for a strict major.minor.patch head.</summary>
    public static string NextPatch(string version)
    {
        var parsed = Parse(version);
        if (parsed.Core.Length != 3 || parsed.PreRelease.Length > 0)
        {
            throw new FormatException($"rule version '{version}' cannot be patch-incremented; expected major.minor.patch");
        }
        int patch = checked(parsed.Core[2] + 1);
        return FormattableString.Invariant($"{parsed.Core[0]}.{parsed.Core[1]}.{patch}");
    }

    private static ParsedVersion Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || !string.Equals(version, version.Trim(), StringComparison.Ordinal))
            throw Malformed(version);

        int firstPlus = version.IndexOf('+', StringComparison.Ordinal);
        if (firstPlus >= 0 && (firstPlus == version.Length - 1 || version.IndexOf('+', firstPlus + 1) >= 0))
            throw Malformed(version);
        string build = firstPlus >= 0 ? version[(firstPlus + 1)..] : string.Empty;
        string withoutBuild = firstPlus >= 0 ? version[..firstPlus] : version;

        int dash = withoutBuild.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0 && dash == withoutBuild.Length - 1) throw Malformed(version);
        string core = dash >= 0 ? withoutBuild[..dash] : withoutBuild;
        string pre = dash >= 0 ? withoutBuild[(dash + 1)..] : string.Empty;

        var segments = core.Split('.');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0)) throw Malformed(version);
        var parsed = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                throw Malformed(version);
            parsed[i] = n;
        }

        return new ParsedVersion(parsed, pre, build);
    }

    private static FormatException Malformed(string? version)
        => new($"malformed rule version '{version ?? "<null>"}'");
}
