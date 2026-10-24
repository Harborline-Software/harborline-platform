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
/// (every aria-labelledby, aria-describedby, aria-controls and label <c>for</c> in a render resolves). Elements
/// may come straight from bUnit's <c>Find</c>/<c>FindAll</c>, and ids resolve within the render the element belongs to.
/// A <c>Find</c> result follows re-renders; <c>FindAll</c> results are snapshots, so re-query them after a re-render.
/// A <c>FindComponent&lt;T&gt;()</c> render is parsed on its own and excludes its parent's markup.</para>
/// <para>Covered accname 1.2 steps:</para>
/// <list type="bullet">
/// <item>2A hidden: the <c>hidden</c> attribute, <c>aria-hidden="true"</c> and an inline <c>display:none</c> or
/// <c>visibility:hidden|collapse</c>, judged as if the root were shown: the root and its ancestors count as
/// shown, so a root inside a collapsed panel is named as it will be once the panel opens. A node referenced
/// directly by aria-labelledby or aria-describedby (or a label) that is hidden apart from that chain still
/// contributes, hidden descendants included. Stylesheet-driven hiding is invisible here. <c>script</c>,
/// <c>style</c>, <c>noscript</c>, <c>template</c>, <c>input type=hidden</c> and SVG <c>desc</c>/<c>metadata</c>
/// never contribute.</item>
/// <item>2B aria-labelledby: every IDREF in attribute order, results joined by a space; a node already
/// inside a labelledby/describedby traversal does not follow its own aria-labelledby; an empty result
/// falls through to the next step.</item>
/// <item>2D aria-label, when not blank.</item>
/// <item>2E host-language label, unless the element is presentational (role none|presentation with no global
/// ARIA attribute and no focusability, per ARIA 1.2 conflict resolution): <c>img</c>/<c>area</c> <c>alt</c>; an
/// SVG element's first <c>title</c> child; and, for labelable elements, each <c>label</c> whose <c>for</c>
/// resolves to it plus a wrapping <c>label</c> whose first labelable descendant it is (several labels join in
/// tree order).</item>
/// <item>2F/2H name from content: for the roles ARIA 1.2 marks "Name From: contents" (the first role token ARIA
/// 1.2 recognises, else the implicit role of button, a/area[href], h1-h6, td, th, tr, option and button-like
/// input types), the first <c>summary</c> of a <c>details</c>, a referenced node or label, and every descendant
/// during that recursion. Inline children concatenate as written; block-level HTML children are separated by a
/// space, as browsers do (accname 1.2 leaves CSS-display spacing as an open note).</item>
/// <item>2G text nodes; the final string has whitespace collapsed and trimmed.</item>
/// <item>2I tooltip: <c>title</c>, last.</item>
/// <item>§4.2 description: aria-describedby (IDREFs in order), else <c>aria-description</c>.</item>
/// </list>
/// <para>Not covered: step 1's roles that prohibit naming; 2C embedded-control values (an input, select,
/// textarea or textbox/combobox/listbox/slider/spinbutton met during recursion contributes nothing, and one
/// referenced directly by aria-labelledby or aria-describedby fails as not covered); CSS generated content
/// (<c>::before</c>/<c>::after</c>); shadow DOM and slots; <c>fieldset</c>/<c>legend</c>, <c>table</c>/<c>caption</c>,
/// <c>figure</c>/<c>figcaption</c> and an input's <c>value</c>/<c>placeholder</c>; <c>title</c> as a description.</para>
/// <para>Deliberate strictness, because this is an assertion helper rather than a user agent: an IDREF (or a
/// label's <c>for</c>) that matches no element or several elements fails, where a browser would skip it or take
/// the first; a reference attribute that is present but names no id fails; an empty name fails; and the element
/// passed in is named even while it is hidden itself, so a collapsed panel's wiring can be asserted (assert its
/// visibility separately).</para>
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

    // ARIA 1.2's non-abstract roles: Role() takes the first token of a role attribute that names one of these.
    private static readonly HashSet<string> Aria12Roles = new(StringComparer.Ordinal)
    {
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox",
        "code", "columnheader", "combobox", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory",
        "document", "emphasis", "feed", "figure", "form", "generic", "grid", "gridcell", "group", "heading", "img",
        "insertion", "link", "list", "listbox", "listitem", "log", "main", "marquee", "math", "menu", "menubar", "menuitem",
        "menuitemcheckbox", "menuitemradio", "meter", "navigation", "none", "note", "option", "paragraph", "presentation",
        "progressbar", "radio", "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar", "search", "searchbox",
        "separator", "slider", "spinbutton", "status", "strong", "subscript", "superscript", "switch", "tab", "table",
        "tablist", "tabpanel", "term", "textbox", "time", "timer", "toolbar", "tooltip", "tree", "treegrid", "treeitem",
    };

    // ARIA 1.2 §6.4 global states and properties: any of them keeps a role=none|presentation element's semantics.
    private static readonly string[] GlobalAriaAttributes =
    [
        "aria-atomic", "aria-busy", "aria-controls", "aria-current", "aria-describedby", "aria-details", "aria-disabled",
        "aria-dropeffect", "aria-errormessage", "aria-flowto", "aria-grabbed", "aria-haspopup", "aria-hidden", "aria-invalid",
        "aria-keyshortcuts", "aria-label", "aria-labelledby", "aria-live", "aria-owns", "aria-relevant", "aria-roledescription",
    ];

    private const string SvgNamespace = "http://www.w3.org/2000/svg";

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

    // Shown: the root and its ancestors. They count as shown, so 2A is judged as if the root were displayed.
    private readonly record struct Traversal(IReadOnlyList<IElement> Scope, IReadOnlySet<INode> Shown, bool InReference, bool IncludeHidden);

    /// <summary>
    /// The accessible name of <paramref name="element"/>. Fails when the name is empty, or when an
    /// aria-labelledby id on the way is missing or duplicated in the rendered markup.
    /// </summary>
    public static string AccessibleName(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element = InTree(element);
        var name = Flatten(TextAlternative(element, Visit.Root, new Traversal(Scope(element), ShownChain(element), InReference: false, IncludeHidden: false)));
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
        if (element.HasAttribute("aria-describedby")) return Flatten(ReferencedText(element, "aria-describedby", scope, ShownChain(element)));
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
        var render = Top(owner);
        for (var index = 0; index < wanted.Count; index++)
        {
            if (!ReferenceEquals(Top(wanted[index]), render))
                throw new XunitException($"expected[{index}] {Describe(wanted[index])} belongs to a different render than {Describe(owner)}; re-query it from the same rendered component after the latest render (FindAll results are snapshots that do not refresh, and a FindComponent<T>() render is parsed separately).");
        }
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
                    if (attribute == "aria-labelledby" && Flatten(ReferencedText(element, attribute, scope, ShownChain(element))).Length == 0)
                        failures.Add($"{Describe(element)}: aria-labelledby resolves to an empty name.");
                }
                catch (XunitException failure)
                {
                    failures.Add(failure.Message);
                }
            }
            if (element.LocalName != "label" || !element.HasAttribute("for")) continue;
            try
            {
                var target = Resolve(element, "for", scope)[0];
                if (!IsLabelable(target))
                    failures.Add($"{Describe(element)}: for references id \"{target.Id}\", which is on a <{target.LocalName}>, not a labelable element.");
            }
            catch (XunitException failure)
            {
                failures.Add(failure.Message);
            }
        }
        if (failures.Count > 0) throw new XunitException($"Id references do not resolve:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    // accname 1.2 §4.3.2 step 2, for one node.
    private static string TextAlternative(INode node, Visit visit, Traversal traversal)
    {
        if (node is IText text) return text.Data;                                                     // 2G
        if (node is not IElement element || NeverContributes(element)) return string.Empty;

        if (visit != Visit.Root && !traversal.IncludeHidden && !traversal.Shown.Contains(element) && IsHidden(element))
            return string.Empty;                                                                       // 2A

        if (!traversal.InReference && element.HasAttribute("aria-labelledby"))                          // 2B
        {
            var labelled = ReferencedText(element, "aria-labelledby", traversal.Scope, traversal.Shown);
            if (Flatten(labelled).Length > 0) return labelled;
        }

        if (visit != Visit.Root && IsEmbeddedControl(element))                                          // 2C: not covered
        {
            if (visit == Visit.Referenced)
                throw new XunitException($"{Describe(element)}: accname 2C embedded-control value not covered; this helper cannot name through a control referenced directly by aria-labelledby or aria-describedby.");
            return string.Empty;
        }

        var ariaLabel = element.GetAttribute("aria-label");                                            // 2D
        if (!string.IsNullOrWhiteSpace(ariaLabel)) return ariaLabel;

        if (!IsPresentational(element))                                                                // 2E
        {
            if (element.LocalName is "img" or "area" && element.HasAttribute("alt")) return element.GetAttribute("alt") ?? string.Empty;
            if (element.NamespaceUri == SvgNamespace
                && element.Children.FirstOrDefault(child => child.LocalName == "title" && child.NamespaceUri == SvgNamespace) is { } svgTitle
                && Flatten(svgTitle.TextContent).Length > 0)
                return svgTitle.TextContent;
        }
        if (visit != Visit.Descendant && IsLabelable(element))
        {
            var labels = string.Join(" ", Labels(element, traversal.Scope).Select(label =>
                TextAlternative(label, Visit.Referenced, traversal with { IncludeHidden = IsHiddenInTree(label, traversal.Shown) })));
            if (Flatten(labels).Length > 0) return labels;
        }

        if (visit != Visit.Root || TakesNameFromContent(element))                                     // 2F, 2H
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
    private static string ReferencedText(IElement element, string attribute, IReadOnlyList<IElement> scope, IReadOnlySet<INode> shown) =>
        string.Join(" ", Resolve(element, attribute, scope).Select(target =>
            TextAlternative(target, Visit.Referenced, new Traversal(scope, shown, InReference: true, IncludeHidden: IsHiddenInTree(target, shown)))));

    private static List<IElement> Resolve(IElement element, string attribute, IReadOnlyList<IElement> scope)
    {
        // aria-* attributes hold a whitespace-separated IDREF list; label's for holds exactly one id, verbatim.
        var value = element.GetAttribute(attribute) ?? string.Empty;
        var ids = attribute == "for"
            ? (string.IsNullOrWhiteSpace(value) ? [] : [value])
            : value.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0) throw new XunitException($"{Describe(element)}: {attribute} is present but names no id.");
        var targets = new List<IElement>(ids.Length);
        foreach (var id in ids)
        {
            var matches = scope.Where(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0) throw new XunitException($"{Describe(element)}: {attribute} references id \"{id}\", which no element in this render carries (the scope is the render the element came from: a FindComponent<T>() render excludes its parent's markup, so query the element from the outer render).");
            if (matches.Count > 1) throw new XunitException($"{Describe(element)}: {attribute} references id \"{id}\", which {matches.Count} elements carry; ids must be unique.");
            targets.Add(matches[0]);
        }
        return targets;
    }

    // The labels of a labelable element, in tree order: each label whose for resolves to it (strictly, so a
    // duplicated id fails as it does for aria-labelledby), and a label without for whose first labelable
    // descendant it is.
    private static IEnumerable<IElement> Labels(IElement element, IReadOnlyList<IElement> scope)
    {
        foreach (var label in scope.Where(candidate => candidate.LocalName == "label"))
        {
            if (label.HasAttribute("for"))
            {
                if (string.IsNullOrEmpty(element.Id) || !string.Equals(label.GetAttribute("for"), element.Id, StringComparison.Ordinal)) continue;
                if (ReferenceEquals(Resolve(label, "for", scope)[0], element)) yield return label;
            }
            else if (ReferenceEquals(label.GetDescendants().OfType<IElement>().FirstOrDefault(IsLabelable), element))
            {
                yield return label;
            }
        }
    }

    // The root and every ancestor up to the document: what 2A treats as shown.
    private static HashSet<INode> ShownChain(IElement root)
    {
        var shown = new HashSet<INode>(ReferenceEqualityComparer.Instance);
        for (INode? current = root; current is not null; current = current.Parent) shown.Add(current);
        return shown;
    }

    // bUnit's Find returns an auto-refreshing wrapper, which is not itself a node of the parsed tree; its
    // sibling and parent links are the tree's, so step out and back in to reach the node the tree holds.
    private static IElement InTree(IElement element) =>
        (element.PreviousSibling is { } previous ? previous.NextSibling : element.Parent?.FirstChild) as IElement ?? element;

    // Every element reachable from the topmost ancestor: bUnit parses each render into its own document.
    private static List<IElement> Scope(IElement element) => Top(element).GetDescendantsAndSelf().OfType<IElement>().ToList();

    // The topmost ancestor: one per bUnit render (and per FindComponent<T>() render).
    private static INode Top(INode node)
    {
        while (node.Parent is { } parent) node = parent;
        return node;
    }

    private static string? Role(IElement element)
    {
        var explicitRole = (element.GetAttribute("role") ?? string.Empty).Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.ToLowerInvariant()).FirstOrDefault(Aria12Roles.Contains);
        if (explicitRole is not null) return explicitRole;
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
        || StyleHidden.IsMatch(element.GetAttribute("style") ?? string.Empty);

    // Hidden by itself or by an ancestor, stopping at the shown chain: a target inside the same collapsed
    // panel as the root is judged as the panel would be once shown.
    private static bool IsHiddenInTree(IElement element, IReadOnlySet<INode> shown)
    {
        for (var current = element; current is not null && !shown.Contains(current); current = current.ParentElement)
            if (IsHidden(current)) return true;
        return false;
    }

    // Never rendered as text, even inside a hidden node referenced directly (2A's exception): script, style,
    // noscript, template (whose content AngleSharp keeps outside the tree anyway), input type=hidden, and SVG
    // desc and metadata. An SVG title is read only by its parent's 2E rule, never as content.
    private static bool NeverContributes(IElement element) =>
        element.LocalName is "script" or "style" or "noscript" or "template"
        || (element.LocalName == "input" && InputType(element) == "hidden")
        || (element.NamespaceUri == SvgNamespace && element.LocalName is "title" or "desc" or "metadata");

    // ARIA 1.2 presentational-role conflict resolution: role none|presentation holds only without a global
    // ARIA attribute and on an element that cannot take focus.
    private static bool IsPresentational(IElement element) =>
        Role(element) is "none" or "presentation"
        && !GlobalAriaAttributes.Any(element.HasAttribute)
        && !element.HasAttribute("tabindex")
        && !(element.LocalName is "a" or "area" && element.HasAttribute("href"))
        && element.LocalName is not ("button" or "input" or "select" or "textarea");

    // A role that takes its name from content, or (with no explicit role) the first summary child of details.
    private static bool TakesNameFromContent(IElement element) =>
        Role(element) is { } role
            ? NameFromContentRoles.Contains(role)
            : element.LocalName == "summary" && element.ParentElement is { LocalName: "details" } details
              && ReferenceEquals(details.Children.FirstOrDefault(child => child.LocalName == "summary"), element);

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
