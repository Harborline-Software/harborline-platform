namespace Harborline.Blocks.BuilderDefinitions;

// Definition syntax only. Allocation, ordering, history and publication belong to T-620's store.
internal static class LayoutVersionSyntax
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var build = value.Split('+', StringSplitOptions.None);
        if (build.Length > 2 || (build.Length == 2 && !IdentifiersValid(build[1], true))) return false;
        var pieces = build[0].Split('-', 2, StringSplitOptions.None);
        var core = pieces[0].Split('.', StringSplitOptions.None);
        return core.Length == 3 && core.All(Number)
            && (pieces.Length == 1 || IdentifiersValid(pieces[1], false));
    }

    private static bool IdentifiersValid(string value, bool numericLeadingZerosAllowed)
        => value.Split('.').All(part => part.Length > 0
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && (numericLeadingZerosAllowed || part.Length == 1 || part[0] != '0' || !part.All(char.IsAsciiDigit)));

    private static bool Number(string value)
        => value.Length > 0 && (value.Length == 1 || value[0] != '0') && value.All(char.IsAsciiDigit);
}
