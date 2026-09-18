namespace Harborline.Blocks.BuilderDefinitions;

// Promoted from LayoutDefinitionStore.cs, handoff/t620-layout-store-source (e2f4bb6c).
internal sealed record DefinitionSemanticVersion(string Major, string Minor, string Patch, string? PreRelease)
    : IComparable<DefinitionSemanticVersion>
{
    public static DefinitionSemanticVersion Parse(string value)
        => TryParse(value, out var parsed) ? parsed : throw new FormatException("definition.version_invalid");

    public static bool TryParse(string? value, out DefinitionSemanticVersion parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var build = value.Split('+', StringSplitOptions.None);
        if (build.Length > 2 || (build.Length == 2 && !IdentifiersValid(build[1], numericLeadingZerosAllowed: true))) return false;
        var pieces = build[0].Split('-', 2, StringSplitOptions.None);
        var core = pieces[0].Split('.', StringSplitOptions.None);
        if (core.Length != 3
            || !Number(core[0])
            || !Number(core[1])
            || !Number(core[2])) return false;
        var pre = pieces.Length == 2 ? pieces[1] : null;
        if (pre is not null && !IdentifiersValid(pre, numericLeadingZerosAllowed: false)) return false;
        parsed = new(core[0], core[1], core[2], pre);
        return true;
    }

    public int CompareTo(DefinitionSemanticVersion? other)
    {
        if (other is null) return 1;
        var result = CompareNumber(Major, other.Major);
        if (result == 0) result = CompareNumber(Minor, other.Minor);
        if (result == 0) result = CompareNumber(Patch, other.Patch);
        if (result != 0) return result;
        if (PreRelease is null || other.PreRelease is null)
            return PreRelease is null ? other.PreRelease is null ? 0 : 1 : -1;
        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var leftNumeric = left[index].All(char.IsAsciiDigit);
            var rightNumeric = right[index].All(char.IsAsciiDigit);
            if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
            var compared = leftNumeric ? left[index].Length.CompareTo(right[index].Length) : 0;
            if (compared == 0) compared = StringComparer.Ordinal.Compare(left[index], right[index]);
            if (compared != 0) return compared;
        }
        return left.Length.CompareTo(right.Length);
    }

    private static bool IdentifiersValid(string value, bool numericLeadingZerosAllowed)
        => value.Split('.').All(part => part.Length > 0
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && (numericLeadingZerosAllowed || part.Length == 1 || part[0] != '0' || !part.All(char.IsAsciiDigit)));

    private static int CompareNumber(string left, string right)
    {
        var length = left.Length.CompareTo(right.Length);
        return length != 0 ? length : StringComparer.Ordinal.Compare(left, right);
    }

    private static bool Number(string value)
        => value.Length > 0
            && (value.Length == 1 || value[0] != '0')
            && value.All(char.IsAsciiDigit);
}
