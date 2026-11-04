using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Foundation.Documents;

/// <summary>The five canonical merge formats (DES-0021 documents-auth-9). The format is chosen per run; the locale is the document's.</summary>
public enum DocumentMergeFormat
{
    /// <summary>The value verbatim.</summary>
    Text = 0,
    /// <summary>A decimal amount in the document locale, with the render context's currency.</summary>
    Currency = 1,
    /// <summary>An ISO-8601 date or date-time as the document locale's short date.</summary>
    Date = 2,
    /// <summary>A grouped whole number in the document locale.</summary>
    Integer = 3,
    /// <summary>A grouped decimal number in the document locale.</summary>
    Decimal = 4,
}

/// <summary>One run of a block's line, already resolved from Layout's text runs by the host seam.</summary>
public abstract record DocumentFlowRun;

/// <summary>Authored literal text, rendered unchanged.</summary>
public sealed record DocumentLiteralRun(string Text) : DocumentFlowRun;

/// <summary>A resolved canonical merge value, formatted in the document locale.</summary>
/// <param name="CanonicalValue">The invariant canonical value (decimal string or ISO-8601); null when the record holds none.</param>
/// <param name="Format">The canonical format chosen for this run.</param>
/// <param name="Fallback">The authored literal printed when the value is null (documents-auth-10).</param>
public sealed record DocumentMergeRun(string? CanonicalValue, DocumentMergeFormat Format, string? Fallback = null) : DocumentFlowRun;

/// <summary>
/// Formats canonical merge values in the document's locale (documents-eng-7). It is deterministic across hosts: only
/// predefined cultures are used, because ICU fabricates patterns for unknown tags, and an unknown tag falls back to
/// en-US rather than to a fabricated or invariant culture.
/// </summary>
internal sealed class DocumentValueFormatter(string localeTag, string currencyCode)
{
    private readonly CultureInfo _culture = ResolveCulture(localeTag);

    public string FormatRuns(IEnumerable<DocumentFlowRun> runs)
        => string.Concat(runs.Select(run => run switch
        {
            DocumentLiteralRun literal => literal.Text,
            DocumentMergeRun merge => merge.CanonicalValue is null ? merge.Fallback ?? string.Empty : Format(merge.CanonicalValue, merge.Format),
            _ => throw new InvalidOperationException("documents.merge.run_unsupported"),
        }));

    private string Format(string value, DocumentMergeFormat format) => format switch
    {
        DocumentMergeFormat.Text => value,
        DocumentMergeFormat.Currency => Decimal(value).ToString("C", CurrencyFormat()),
        DocumentMergeFormat.Date => Date(value),
        DocumentMergeFormat.Integer => Decimal(value).ToString("N0", _culture),
        // The value's own scale: "N" alone pads to the culture's digit count, printing a precision the value never had.
        DocumentMergeFormat.Decimal => Decimal(value) is var number ? number.ToString("N" + number.Scale.ToString(CultureInfo.InvariantCulture), _culture) : value,
        _ => throw new InvalidOperationException("documents.merge.format_unsupported"),
    };

    private NumberFormatInfo CurrencyFormat()
    {
        var format = (NumberFormatInfo)_culture.NumberFormat.Clone();
        format.CurrencySymbol = currencyCode.ToUpperInvariant() switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "JPY" => "¥",
            "CAD" => "CA$",
            "AUD" => "A$",
            var other => other,
        };
        return format;
    }

    private static decimal Decimal(string value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new InvalidOperationException("documents.merge.value_malformed");

    private string Date(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.ToString("d", _culture);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant)
            ? instant.ToString("d", _culture)
            : throw new InvalidOperationException("documents.merge.value_malformed");
    }

    private static CultureInfo ResolveCulture(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }
}

/// <summary>
/// The deterministic hash of a rendered document's semantic content (documents-ck-19, documents-eng-12,
/// documents-run-1): its type, locale, currency, style and resolved blocks. PDF bytes are not deterministic, so bytes
/// never enter it. Fields are written as a JSON array, so no two different documents share a serialization.
/// </summary>
public static class DocumentContentHash
{
    /// <summary>Returns <c>sha256:</c> and 64 lowercase hex digits over the document's semantic content.</summary>
    public static string Of(RenderedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(document.DocumentType);
            writer.WriteStringValue(document.LocaleTag);
            writer.WriteStringValue(document.CurrencyCode);
            writer.WriteStringValue(document.Style?.BrandName);
            writer.WriteStringValue(document.Style?.LogoAssetRef);
            foreach (var block in document.Blocks)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(block.Id);
                writer.WriteStringValue(block.PageVariant);
                writer.WriteStringValue(block.Content);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }
}

/// <summary>
/// A developer output writer as named and parameterised by the host (documents-bound-1). The pipeline sees the format
/// and never the library behind it.
/// </summary>
/// <param name="Name">The adapter's name, for diagnostics and authoring.</param>
/// <param name="Writer">The library-neutral writer.</param>
/// <param name="Parameters">The adapter's declared parameters.</param>
public sealed record DocumentOutputWriterBinding(string Name, IDocumentOutputWriter Writer, IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>
/// One writer per format (documents-eng-9). A host swaps a library by changing which bindings it registers, through
/// its own DI container; this type needs only the enumerable.
/// </summary>
public sealed class DocumentOutputWriters
{
    private readonly Dictionary<string, DocumentOutputWriterBinding> _byFormat = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the registry. Two bindings for one format are refused, because neither would be authoritative.</summary>
    public DocumentOutputWriters(IEnumerable<DocumentOutputWriterBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        foreach (var binding in bindings)
        {
            if (!_byFormat.TryAdd(binding.Writer.Format, binding))
                throw new InvalidOperationException("documents.writer.format_duplicate");
        }
    }

    /// <summary>Returns the binding registered for a format; an unregistered format is refused, never defaulted.</summary>
    public DocumentOutputWriterBinding Resolve(string format)
        => _byFormat.TryGetValue(format, out var binding)
            ? binding
            : throw new InvalidOperationException("documents.writer.format_unknown");
}

/// <summary>
/// The style-asset boundary (documents-bound-8). A template logo is a digest-pinned pack asset (DES-0054 ruling 9), so
/// only a digest reference passes. A URL or path could make the host fetch an attacker-chosen resource (CWE-918) or
/// serve unpinned active content such as script in an SVG (CWE-79) when a preview renders.
/// </summary>
internal static class DocumentStyleAssets
{
    private const string Prefix = "sha256:";

    public static void RefuseUnpinned(TemplateStyle? style)
    {
        if (style?.LogoAssetRef is { } reference && !IsPinned(reference))
            throw new InvalidOperationException("documents.style.logo_not_pinned");
    }

    private static bool IsPinned(string reference)
        => reference.Length == Prefix.Length + 64
            && reference.StartsWith(Prefix, StringComparison.Ordinal)
            && reference.AsSpan(Prefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;
}
