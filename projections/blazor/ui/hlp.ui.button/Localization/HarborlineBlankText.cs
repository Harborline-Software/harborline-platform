using System.Diagnostics.CodeAnalysis;

namespace Harborline.UIAdapters.Blazor.Localization;

/// <summary>The blank test the two UI lanes share (T-1107): text made only of these characters is no text.</summary>
/// <remarks>
/// The characters are ECMA-262's WhiteSpace and LineTerminator productions, the 25 that
/// <c>String.prototype.trim</c> strips, so the React lane's <c>trim()</c> test agrees with this one. They are written
/// out because .NET's <c>char.IsWhiteSpace</c> differs at two of them: it counts U+0085 (NEL) as white space and
/// U+FEFF (ZWNBSP) as not. Here a NEL-only text is not blank and a ZWNBSP-only one is. The list is the same as
/// <c>BlankNameCharacters</c> in T-1107's HarborlineToaster (commit bb02a8e4).
/// </remarks>
internal static class HarborlineBlankText
{
    internal const string Characters = "\t\n\v\f\r \u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF";

    /// <summary>True when <paramref name="text"/> is null, empty, or made only of <see cref="Characters"/>.</summary>
    internal static bool IsBlank([NotNullWhen(false)] string? text) => text is null || text.AsSpan().IndexOfAnyExcept(Characters) < 0;
}
