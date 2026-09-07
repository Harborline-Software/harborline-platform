using System.Collections;
using System.Text.RegularExpressions;

namespace Harborline.Foundation;

/// <summary>Projection-neutral class composition with clsx flattening and Tailwind-style conflict resolution.</summary>
public static partial class CssClassComposer
{
    private static readonly HashSet<string> DisplayUtilities = new(StringComparer.Ordinal)
    {
        "block", "inline-block", "inline", "flex", "inline-flex", "table", "inline-table",
        "table-caption", "table-cell", "table-column", "table-column-group", "table-footer-group",
        "table-header-group", "table-row-group", "table-row", "flow-root", "grid", "inline-grid",
        "contents", "list-item", "hidden",
    };

    /// <summary>Combines strings, nested enumerables, and ordered conditional entries.</summary>
    public static string Combine(params object?[] inputs)
    {
        var tokens = new List<string>();
        foreach (var input in inputs) Flatten(input, tokens);
        return Merge(tokens);
    }

    /// <summary>Merges an already flattened class sequence.</summary>
    public static string Merge(IEnumerable<string> tokens)
    {
        var output = new List<(string Token, string? Group)>();
        foreach (var token in tokens.SelectMany(SplitTokens))
        {
            var group = ConflictGroup(token);
            if (group is not null)
            {
                for (var index = output.Count - 1; index >= 0; index--)
                {
                    if (output[index].Group == group)
                    {
                        output.RemoveAt(index);
                        break;
                    }
                }
            }

            output.Add((token, group));
        }

        return string.Join(' ', output.Select(item => item.Token));
    }

    private static void Flatten(object? value, List<string> tokens)
    {
        switch (value)
        {
            case null or false:
                return;
            case string text when !string.IsNullOrWhiteSpace(text):
                tokens.Add(text);
                return;
            case KeyValuePair<string, bool> entry when entry.Value:
                tokens.Add(entry.Key);
                return;
            case KeyValuePair<string, bool>:
                return;
            case IEnumerable<KeyValuePair<string, bool>> entries:
                foreach (var entry in entries) Flatten(entry, tokens);
                return;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is string key && entry.Value is true) tokens.Add(key);
                }
                return;
            case IEnumerable enumerable:
                foreach (var item in enumerable) Flatten(item, tokens);
                return;
            case true:
                return;
            default:
                tokens.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                return;
        }
    }

    private static IEnumerable<string> SplitTokens(string input) =>
        input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? ConflictGroup(string token)
    {
        var (modifier, utility) = SplitModifier(token);
        var important = utility.EndsWith('!');
        var normalized = important ? utility[..^1] : utility;
        var importantScope = important ? "!" : string.Empty;

        if (DisplayUtilities.Contains(normalized)) return modifier + importantScope + "display";
        if (normalized is "sr-only" or "not-sr-only") return modifier + importantScope + "sr";

        var match = UtilityPrefix().Match(normalized);
        if (!match.Success) return null;
        var prefix = match.Groups[1].Value;
        var group = prefix switch
        {
            "p" or "px" or "py" or "ps" or "pe" or "pt" or "pr" or "pb" or "pl" => "padding:" + prefix,
            "m" or "mx" or "my" or "ms" or "me" or "mt" or "mr" or "mb" or "ml" => "margin:" + prefix,
            "w" or "min-w" or "max-w" or "h" or "min-h" or "max-h" => "size:" + prefix,
            "bg" => "background",
            "border" => BorderGroup(normalized),
            "rounded" => RoundedGroup(normalized),
            "outline" => "outline",
            "opacity" => "opacity",
            "z" => "z-index",
            "order" => "order",
            "gap" or "gap-x" or "gap-y" => prefix,
            "grid-cols" or "grid-rows" or "col-span" or "row-span" => prefix,
            "items" or "justify" or "content" or "self" or "place-items" => prefix,
            "font" or "leading" or "tracking" or "text" => TextGroup(normalized, prefix),
            "inset" or "inset-x" or "inset-y" or "top" or "right" or "bottom" or "left" or "start" or "end" => "position:" + prefix,
            "translate-x" or "translate-y" or "scale" or "rotate" or "skew-x" or "skew-y" => "transform:" + prefix,
            _ => prefix,
        };
        return modifier + importantScope + group;
    }

    private static (string Modifier, string Utility) SplitModifier(string token)
    {
        var bracketDepth = 0;
        for (var index = token.Length - 1; index >= 0; index--)
        {
            bracketDepth += token[index] switch { ']' => 1, '[' => -1, _ => 0 };
            if (token[index] == ':' && bracketDepth == 0) return (token[..(index + 1)], token[(index + 1)..]);
        }

        return (string.Empty, token);
    }

    private static string BorderGroup(string utility)
    {
        if (Regex.IsMatch(utility, "^border(?:-[xytrbles])?-(?:0|2|4|8|\\[)")) return "border-width:" + utility.Split('-', 2)[0];
        return "border-color:" + string.Join('-', utility.Split('-').Take(utility.StartsWith("border-") ? 1 : 2));
    }

    private static string RoundedGroup(string utility) => utility.StartsWith("rounded-") && Regex.IsMatch(utility, "^rounded-(?:[trbl]|[trbl][trbl])-")
        ? string.Join('-', utility.Split('-').Take(2))
        : "rounded";

    private static string TextGroup(string utility, string prefix)
    {
        if (prefix != "text") return prefix;
        return Regex.IsMatch(utility, "^text-(?:xs|sm|base|lg|xl|[2-9]xl|\\[.*(?:px|rem|em|%|vw|vh).*)$") ? "text-size" : "text-color";
    }

    [GeneratedRegex("^-?(p|px|py|ps|pe|pt|pr|pb|pl|m|mx|my|ms|me|mt|mr|mb|ml|w|min-w|max-w|h|min-h|max-h|bg|border|rounded|outline|opacity|z|order|gap|gap-x|gap-y|grid-cols|grid-rows|col-span|row-span|items|justify|content|self|place-items|font|leading|tracking|text|inset|inset-x|inset-y|top|right|bottom|left|start|end|translate-x|translate-y|scale|rotate|skew-x|skew-y)-")]
    private static partial Regex UtilityPrefix();
}
