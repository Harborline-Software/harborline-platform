namespace Harborline.Foundation.RuleAuthoring;

/// <summary>Pure advisory suggestions; only the shared store can admit a new identity.</summary>
public static class RuleKeySuggestion
{
    /// <summary>Suggests a rule key without a version suffix; does not reserve or allocate it.</summary>
    public static string Suggest(string name, IReadOnlySet<string> existing)
    {
        var slug = new System.Text.StringBuilder();
        bool pendingDash = false;
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && slug.Length > 0) slug.Append('-');
                pendingDash = false;
                slug.Append(c);
            }
            else
            {
                pendingDash = true;
            }
        }
        string baseKey = slug.Length > 0 ? slug.ToString() : "rule";
        string candidate = baseKey;
        int n = 2;
        while (existing.Contains(candidate))
        {
            candidate = FormattableString.Invariant($"{baseKey}-{n}");
            n += 1;
        }
        return candidate;
    }
}
