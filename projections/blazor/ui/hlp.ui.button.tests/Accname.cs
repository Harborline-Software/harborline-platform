using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit.Sdk;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Accessible name, accessible description and id-reference wiring for bUnit-rendered markup (control T-944).
/// <para>
/// Prior art: W3C Accessible Name and Description Computation 1.2 (accname 1.2,
/// https://www.w3.org/TR/accname-1.2/, §4.2 description and §4.3.2 computation steps), with the HTML parts
/// taken from HTML-AAM; R-0125 B3 and B6. bUnit renders through AngleSharp with no CSS, layout or browser,
/// so this computes what the markup alone determines. An exact-name assertion is what kills a mutant that
/// blanks or misroutes a name or an id: axe only checks that some name exists (R-0125 B6).
/// </para>
/// <para>API: <see cref="AccessibleName"/>, <see cref="AccessibleDescription"/>, <see cref="AssertReferences"/>
/// (one id-reference attribute points at exactly these elements) and <see cref="AssertIdReferencesResolve{TComponent}"/>
/// (every aria-labelledby, aria-describedby and aria-controls in a render resolves). Elements may come straight
/// from bUnit's <c>Find</c>/<c>FindAll</c>; ids resolve within the render the element belongs to.</para>
/// <para>Covered accname 1.2 steps:</para>
/// <list type="bullet">
/// <item>2A hidden: the <c>hidden</c> attribute, <c>aria-hidden="true"</c>, an inline <c>display:none</c> or
/// <c>visibility:hidden|collapse</c>, <c>input type=hidden</c>, <c>script</c>, <c>style</c> and
/// <c>template</c>. A node referenced directly by aria-labelledby or aria-describedby (or a label) that is
/// hidden still contributes, hidden descendants included. Stylesheet-driven hiding is invisible here.</item>
/// <item>2B aria-labelledby: every IDREF in attribute order, results joined by a space; a node already
/// inside a labelledby/describedby traversal does not follow its own aria-labelledby; an empty result
/// falls through to the next step.</item>
/// <item>2D aria-label, when not blank.</item>
/// <item>2E host-language label: <c>label[for]</c> and a wrapping <c>label</c> for labelable elements
/// (several labels join in tree order), and <c>img</c>/<c>area</c> <c>alt</c>.</item>
/// <item>2F/2H name from content: for the roles ARIA 1.2 marks "Name From: contents" (explicit
/// <c>role</c>, or the implicit role of button, a/area[href], h1-h6, td, th, tr, option and button-like
/// input types), for a referenced node or label, and for every descendant during that recursion. Inline
/// children concatenate as written; block-level HTML children are separated by a space, as browsers do
/// (accname 1.2 leaves CSS-display spacing as an open note).</item>
/// <item>2G text nodes; the final string has whitespace collapsed and trimmed.</item>
/// <item>2I tooltip: <c>title</c>, last.</item>
/// <item>§4.2 description: aria-describedby (IDREFs in order), else <c>aria-description</c>.</item>
/// </list>
/// <para>Not covered: step 1's roles that prohibit naming; 2C embedded-control values (an input, select or
/// textarea met during recursion contributes nothing); CSS generated content (<c>::before</c>/<c>::after</c>);
/// shadow DOM and slots; SVG <c>title</c>; <c>fieldset</c>/<c>legend</c>, <c>table</c>/<c>caption</c>,
/// <c>figure</c>/<c>figcaption</c> and an input's <c>value</c>/<c>placeholder</c>; <c>title</c> as a description.</para>
/// <para>Deliberate strictness, because this is an assertion helper rather than a user agent: an IDREF that
/// matches no element or several elements fails (a browser would skip it); a reference attribute that is
/// present but names no id fails; an empty name fails; and the element passed in is named even while it is
/// hidden itself, so a collapsed panel's wiring can be asserted (assert its visibility separately).</para>
/// </summary>
internal static class Accname
{
    /// <summary>The id-reference attributes <see cref="AssertIdReferencesResolve{TComponent}"/> checks.</summary>
    private static readonly string[] WiringAttributes = ["aria-labelledby", "aria-describedby", "aria-controls"];

    private static readonly char[] AsciiWhitespace = [' ', '\t', '\n', '\f', '\r'];

    // ARIA 1.2 roles whose characteristics table says "Name From: contents" (author is also allowed).
    private static readonly HashSet<string> NameFromContentRoles = new(StringComparer.Ordinal)
    {
        "button", "cell", "checkbox", "columnheader", "gridcell", "heading", "link", "menuitem", "menuitemcheckbox",
        "menuitemradio", "option", "radio", "row", "rowheader", "switch", "tab", "tooltip", "treeitem",
    };

    // HTML elements whose default display is not inline; their text is set apart by a space.
    private static readonly HashSet<string> BlockElements = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "br", "dd", "details", "dialog", "div", "dl", "dt", "fieldset",
        "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "nav",
        "ol", "p", "pre", "section", "summary", "table", "td", "th", "tr", "ul",
    };

    private static readonly Regex StyleHidden = new(@"(^|;)\s*(display\s*:\s*none|visibility\s*:\s*(hidden|collapse))\s*(!important\s*)?(;|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private enum Visit { Root, Referenced, Descendant }

    private readonly record struct Traversal(IReadOnlyList<IElement> Scope, bool InReference, bool IncludeHidden);

    /// <summary>
    /// The accessible name of <paramref name="element"/>. Fails when the name is empty, or when an
    /// aria-labelledby id on the way is missing or duplicated in the rendered markup.
    /// </summary>
    public static string AccessibleName(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element = InTree(element);
        var name = Flatten(TextAlternative(element, Visit.Root, new Traversal(Scope(element), InReference: false, IncludeHidden: false)));
        if (name.Length == 0) throw new XunitException($"{Describe(element)} has no accessible name (accname 1.2: no aria-labelledby text, aria-label, native label, name from content or title).");
        return name;
    }

    /// <summary>
    /// The accessible description of <paramref name="element"/>: its aria-describedby targets in order, else
    /// its aria-description, else "". Fails when an aria-describedby id is missing or duplicated.
    /// </summary>
    public static string AccessibleDescription(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element = InTree(element);
        var scope = Scope(element);
        if (element.HasAttribute("aria-describedby")) return Flatten(ReferencedText(element, "aria-describedby", scope));
        return Flatten(element.GetAttribute("aria-description") ?? string.Empty);
    }

    /// <summary>
    /// The id-reference <paramref name="attribute"/> (aria-controls, aria-labelledby, aria-describedby,
    /// aria-owns, ...) on <paramref name="owner"/> resolves to exactly <paramref name="expected"/>, in order, by
    /// element identity. Fails when the attribute is absent, names no id, or names an id that no element or
    /// more than one element carries.
    /// </summary>
    public static void AssertReferences(IElement owner, string attribute, params IElement[] expected)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        ArgumentNullException.ThrowIfNull(expected);
        owner = InTree(owner);
        if (!owner.HasAttribute(attribute)) throw new XunitException($"{Describe(owner)} has no {attribute}.");
        var actual = Resolve(owner, attribute, Scope(owner));
        var wanted = expected.Select(InTree).ToList();
        if (!actual.SequenceEqual(wanted, ReferenceEqualityComparer.Instance))
            throw new XunitException($"{Describe(owner)}: {attribute} resolves to [{string.Join(", ", actual.Select(Describe))}], expected [{string.Join(", ", wanted.Select(Describe))}].");
    }

    /// <summary>
    /// Every aria-labelledby, aria-describedby and aria-controls in <paramref name="rendered"/> names at least
    /// one id, each id is carried by exactly one rendered element, and each aria-labelledby yields a non-empty
    /// name. All failures are reported together.
    /// </summary>
    public static void AssertIdReferencesResolve<TComponent>(IRenderedComponent<TComponent> rendered) where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(rendered);
        var scope = rendered.Nodes.SelectMany(node => node.GetDescendantsAndSelf()).OfType<IElement>().ToList();
        var failures = new List<string>();
        foreach (var element in scope)
        {
            foreach (var attribute in WiringAttributes)
            {
                if (!element.HasAttribute(attribute)) continue;
                try
                {
                    Resolve(element, attribute, scope);
                    if (attribute == "aria-labelledby" && Flatten(ReferencedText(element, attribute, scope)).Length == 0)
                        failures.Add($"{Describe(element)}: aria-labelledby resolves to an empty name.");
                }
                catch (XunitException failure)
                {
                    failures.Add(failure.Message);
                }
            }
        }
        if (failures.Count > 0) throw new XunitException($"Id references do not resolve:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    // accname 1.2 §4.3.2 step 2, for one node.
    private static string TextAlternative(INode node, Visit visit, Traversal traversal)
    {
        if (node is IText text) return text.Data;                                                     // 2G
        if (node is not IElement element) return string.Empty;

        if (visit != Visit.Root && !traversal.IncludeHidden && IsHidden(element)) return string.Empty;   // 2A

        if (!traversal.InReference && element.HasAttribute("aria-labelledby"))                          // 2B
        {
            var labelled = ReferencedText(element, "aria-labelledby", traversal.Scope);
            if (Flatten(labelled).Length > 0) return labelled;
        }

        if (visit == Visit.Descendant && IsEmbeddedControl(element)) return string.Empty;              // 2C: not covered

        var ariaLabel = element.GetAttribute("aria-label");                                            // 2D
        if (!string.IsNullOrWhiteSpace(ariaLabel)) return ariaLabel;

        if (element.LocalName is "img" or "area" && element.HasAttribute("alt"))                       // 2E
            return element.GetAttribute("alt") ?? string.Empty;
        if (visit != Visit.Descendant && IsLabelable(element))
        {
            var labels = string.Join(" ", Labels(element, traversal.Scope).Select(label =>
                TextAlternative(label, Visit.Referenced, traversal with { IncludeHidden = IsHiddenInTree(label) })));
            if (Flatten(labels).Length > 0) return labels;
        }

        if (visit != Visit.Root || NameFromContentRoles.Contains(Role(element) ?? string.Empty))      // 2F, 2H
        {
            var content = string.Concat(element.ChildNodes.Select(child =>
            {
                var part = TextAlternative(child, Visit.Descendant, traversal);
                return child is IElement block && BlockElements.Contains(block.LocalName) ? $" {part} " : part;
            }));
            if (Flatten(content).Length > 0) return content;
        }

        return element.GetAttribute("title") ?? string.Empty;                                         // 2I
    }

    // 2B / §4.2: the text of each referenced node, in attribute order, joined by a space.
    private static string ReferencedText(IElement element, string attribute, IReadOnlyList<IElement> scope) =>
        string.Join(" ", Resolve(element, attribute, scope).Select(target =>
            TextAlternative(target, Visit.Referenced, new Traversal(scope, InReference: true, IncludeHidden: IsHiddenInTree(target)))));

    private static List<IElement> Resolve(IElement element, string attribute, IReadOnlyList<IElement> scope)
    {
        var ids = (element.GetAttribute(attribute) ?? string.Empty).Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0) throw new XunitException($"{Describe(element)}: {attribute} is present but names no id.");
        var targets = new List<IElement>(ids.Length);
        foreach (var id in ids)
        {
            var matches = scope.Where(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0) throw new XunitException($"{Describe(element)}: {attribute} references id \"{id}\", which is not in the rendered markup.");
            if (matches.Count > 1) throw new XunitException($"{Describe(element)}: {attribute} references id \"{id}\", which {matches.Count} elements carry; ids must be unique.");
            targets.Add(matches[0]);
        }
        return targets;
    }

    // The labels of a labelable element, in tree order: label[for=its id], and the label wrapping it.
    private static IEnumerable<IElement> Labels(IElement element, IReadOnlyList<IElement> scope) =>
        scope.Where(label => label.LocalName == "label" && (label.HasAttribute("for")
            ? !string.IsNullOrEmpty(element.Id) && string.Equals(label.GetAttribute("for"), element.Id, StringComparison.Ordinal)
            : ReferenceEquals(label.GetDescendants().OfType<IElement>().FirstOrDefault(IsLabelable), element)));

    // bUnit's Find returns an auto-refreshing wrapper, which is not itself a node of the parsed tree; its
    // sibling and parent links are the tree's, so step out and back in to reach the node the tree holds.
    private static IElement InTree(IElement element) =>
        (element.PreviousSibling is { } previous ? previous.NextSibling : element.Parent?.FirstChild) as IElement ?? element;

    // Every element reachable from the topmost ancestor: bUnit parses each render into its own document.
    private static List<IElement> Scope(IElement element)
    {
        INode top = element;
        while (top.Parent is { } parent) top = parent;
        return top.GetDescendantsAndSelf().OfType<IElement>().ToList();
    }

    private static string? Role(IElement element)
    {
        var explicitRole = element.GetAttribute("role")?.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (explicitRole is not null) return explicitRole.ToLowerInvariant();
        return element.LocalName switch
        {
            "button" => "button",
            "a" or "area" when element.HasAttribute("href") => "link",
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
            "td" => "cell",
            "th" => "columnheader",
            "tr" => "row",
            "option" => "option",
            "input" => InputType(element) switch
            {
                "button" or "submit" or "reset" or "image" => "button",
                "checkbox" => "checkbox",
                "radio" => "radio",
                _ => null,
            },
            _ => null,
        };
    }

    private static string InputType(IElement element) => (element.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();

    private static bool IsHidden(IElement element) =>
        element.HasAttribute("hidden")
        || string.Equals(element.GetAttribute("aria-hidden")?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        || StyleHidden.IsMatch(element.GetAttribute("style") ?? string.Empty)
        || element.LocalName is "script" or "style" or "template"
        || (element.LocalName == "input" && InputType(element) == "hidden");

    private static bool IsHiddenInTree(IElement element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
            if (IsHidden(current)) return true;
        return false;
    }

    private static bool IsLabelable(IElement element) =>
        element.LocalName is "button" or "meter" or "output" or "progress" or "select" or "textarea"
        || (element.LocalName == "input" && InputType(element) != "hidden");

    private static bool IsEmbeddedControl(IElement element) =>
        element.LocalName is "select" or "textarea"
        || (element.LocalName == "input" && Role(element) is null)
        || Role(element) is "textbox" or "combobox" or "listbox" or "slider" or "spinbutton";

    private static string Flatten(string text) => Whitespace.Replace(text, " ").Trim();

    // The element's start tag, for failure messages: enough to find it without dumping its subtree. bUnit's
    // own blazor:* bookkeeping attributes (event handler and element-reference ids) are left out.
    private static string Describe(IElement element)
    {
        var attributes = string.Concat(element.Attributes
            .Where(attribute => !attribute.Name.StartsWith("blazor:", StringComparison.Ordinal))
            .Select(attribute => $" {attribute.Name}=\"{attribute.Value}\""));
        var tag = $"<{element.LocalName}{attributes}>";
        return tag.Length <= 200 ? tag : $"{tag[..197]}...";
    }
}
