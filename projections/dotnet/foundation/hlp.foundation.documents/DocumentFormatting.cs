using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
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
public abstract record DocumentFlowRun
{
    /// <summary>The two run kinds are the whole set; a host cannot add a third.</summary>
    private protected DocumentFlowRun() { }
}

/// <summary>Authored literal text, rendered unchanged.</summary>
public sealed record DocumentLiteralRun(string Text) : DocumentFlowRun;

/// <summary>A resolved canonical merge value, formatted in the document locale.</summary>
/// <param name="CanonicalValue">The invariant canonical value (decimal string or ISO-8601); null when the record holds none.</param>
/// <param name="Format">The canonical format chosen for this run.</param>
/// <param name="Fallback">The authored literal printed when the value is null (documents-auth-10).</param>
public sealed record DocumentMergeRun(string? CanonicalValue, DocumentMergeFormat Format, string? Fallback = null) : DocumentFlowRun;

/// <summary>
/// Formats canonical merge values in the document's locale (documents-eng-7). It is deterministic across hosts and
/// days: only predefined cultures are used, because ICU fabricates patterns for unknown tags, and only strict canonical
/// forms parse, so a value that could be misread (a grouped "1,5", a time with no date) is refused, never guessed at.
/// </summary>
internal sealed class DocumentValueFormatter(CultureInfo culture, string? currencyCode)
{
    private static readonly string[] CanonicalInstants =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    public string FormatRuns(IEnumerable<DocumentFlowRun> runs)
        => string.Concat(runs.Select(run => run is DocumentLiteralRun literal ? literal.Text : FormatMerge((DocumentMergeRun)run)));

    /// <summary>The predefined culture for a tag; an unknown tag is en-US on every host, never a fabricated culture.</summary>
    public static CultureInfo ResolveCulture(string tag)
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

    private string FormatMerge(DocumentMergeRun merge)
        => merge.CanonicalValue is null ? merge.Fallback ?? string.Empty : Format(merge.CanonicalValue, merge.Format);

    private string Format(string value, DocumentMergeFormat format) => format switch
    {
        DocumentMergeFormat.Text => value,
        DocumentMergeFormat.Currency => Currency(value),
        DocumentMergeFormat.Date => Date(value),
        DocumentMergeFormat.Integer => Integer(value),
        // The value's own scale: "N" alone pads to the culture's digit count, printing a precision the value never had.
        DocumentMergeFormat.Decimal => WithOwnScale(CanonicalDecimal(value)),
        _ => throw new InvalidOperationException("documents.merge.format_unsupported"),
    };

    private string WithOwnScale(decimal number) => number.ToString("N" + number.Scale.ToString(CultureInfo.InvariantCulture), culture);

    private string Integer(string value)
    {
        var number = CanonicalDecimal(value);
        return number == decimal.Truncate(number) ? number.ToString("N0", culture) : throw Malformed();
    }

    // ISO 4217 sets the digits, not the culture: a culture's own currency digits would print USD with no cents under
    // ja-JP. An amount finer than the currency's minor unit is refused rather than rounded, since it would be hashed.
    private string Currency(string value)
    {
        if (currencyCode is not { Length: 3 } code || !code.All(char.IsAsciiLetter))
            throw new InvalidOperationException("documents.merge.currency_invalid");
        var upper = code.ToUpperInvariant();
        var digits = MinorUnits(upper);
        var amount = CanonicalDecimal(value);
        if (decimal.Round(amount, digits) != amount) throw Malformed();
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = Symbol(upper);
        format.CurrencyDecimalDigits = digits;
        return amount.ToString("C", format);
    }

    private static int MinorUnits(string code) => code switch
    {
        "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF" or "UGX" or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
        "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
        "CLF" or "UYW" => 4,
        _ => 2,
    };

    private static string Symbol(string code) => code switch
    {
        "USD" => "$",
        "EUR" => "€",
        "GBP" => "£",
        "JPY" => "¥",
        "CAD" => "CA$",
        "AUD" => "A$",
        _ => code,
    };

    // Canonical form only: an optional minus, digits, and optional fraction digits, in invariant notation.
    private static decimal CanonicalDecimal(string value)
    {
        var digits = value.StartsWith('-') ? value[1..] : value;
        var point = digits.IndexOf('.');
        var whole = point < 0 ? digits : digits[..point];
        var fraction = point < 0 ? "0" : digits[(point + 1)..];
        if (whole.Length == 0 || fraction.Length == 0 || !whole.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit))
            throw Malformed();
        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Malformed();
    }

    // ISO-8601 only. A date-time carries its own offset, so the host's time zone and today's date never enter.
    private string Date(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.ToString("d", culture);
        return DateTimeOffset.TryParseExact(value, CanonicalInstants, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant.ToString("d", culture)
            : throw Malformed();
    }

    private static InvalidOperationException Malformed() => new("documents.merge.value_malformed");
}

/// <summary>
/// The deterministic hash of a rendered document's semantic content (documents-ck-19, documents-eng-12,
/// documents-run-1): its type, locale, currency, style and resolved blocks. PDF bytes are not deterministic, so bytes
/// never enter it. Fields are written as a compact JSON array with non-ASCII text left as raw UTF-8, so no two
/// different documents share a serialization and another implementation can reproduce it.
/// </summary>
public static class DocumentContentHash
{
    /// <summary>Returns <c>sha256:</c> and 64 lowercase hex digits over the document's semantic content.</summary>
    public static string Of(RenderedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
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
