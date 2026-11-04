using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
/// Formats canonical merge values in the document's locale (documents-eng-7). The host's clock and time zone never
/// enter, only predefined cultures are used, because ICU fabricates patterns for unknown tags, and only strict
/// canonical forms parse, so a value that could be misread (a grouped "1,5", a time with no date) is refused, never
/// guessed at. The printed text still follows the host's ICU data, which is why an issued record stores its hash.
/// </summary>
internal sealed partial class DocumentValueFormatter(CultureInfo culture, string? currencyCode)
{
    private static readonly string[] CanonicalInstants = ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    public string FormatRuns(IEnumerable<DocumentFlowRun> runs)
        => string.Concat(runs.Select(run => run is DocumentLiteralRun literal ? literal.Text : FormatMerge((DocumentMergeRun)run)));

    /// <summary>
    /// The predefined culture for a BCP 47 language[-script][-region][-variant] tag. A well-formed tag with no
    /// predefined culture is en-US, never a fabricated culture. Any other shape is refused: an underscore would change
    /// the reported name, and a Unicode extension such as -u-nu-arab would change the digits.
    /// </summary>
    public static CultureInfo ResolveCulture(string tag)
    {
        if (!IsWellFormedLocaleTag(tag)) throw new InvalidOperationException(TemplateDefinitionCodes.LocaleInvalid);
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
            // Linux ICU resolves "und" to the invariant culture, whose empty name would report no locale at all.
            return culture.Name.Length == 0 ? CultureInfo.GetCultureInfo("en-US") : culture;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }

    /// <summary>Whether a tag has the shape <see cref="ResolveCulture"/> accepts; publish refuses a fixed tag that lacks it.</summary>
    public static bool IsWellFormedLocaleTag(string tag) => LocaleTag().IsMatch(tag);

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

    /// <summary>The currency code an issued document reports: ISO 4217 upper case, so spellings of one code hash equally.</summary>
    public static string NormalizeCurrency(string code)
        => code is { Length: 3 } && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : code;

    // Canonical form only: the value must be exactly how the invariant decimal prints it. That refuses a sign, spaces,
    // grouping, leading zeros, a bare point, negative zero, and a value with more digits than a decimal holds, which
    // would otherwise be rounded silently.
    private static decimal CanonicalDecimal(string value)
        => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            && number.ToString(CultureInfo.InvariantCulture) == value
                ? number
                : throw Malformed();

    // ISO-8601 only. A date-time carries its own Z or offset, so the host's time zone and today's date never enter.
    private string Date(string value)
    {
        try
        {
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date.ToString("d", culture);
            return CanonicalInstant().IsMatch(value)
                && DateTimeOffset.TryParseExact(value, CanonicalInstants, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
                    ? instant.ToString("d", culture)
                    : throw Malformed();
        }
        catch (ArgumentOutOfRangeException)
        {
            // A date outside the culture's own calendar, such as year 1 under fa-IR's Persian calendar.
            throw new InvalidOperationException("documents.merge.date_outside_calendar");
        }
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T([01][0-9]|2[0-3]):[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalInstant();

    [GeneratedRegex(@"\A[A-Za-z]{2,3}(-[A-Za-z]{4})?(-([A-Za-z]{2}|[0-9]{3}))?(-([0-9A-Za-z]{5,8}|[0-9][0-9A-Za-z]{3}))*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LocaleTag();

    private static InvalidOperationException Malformed() => new("documents.merge.value_malformed");
}

/// <summary>
/// The deterministic hash of a rendered document's semantic content (documents-ck-19, documents-eng-12,
/// documents-run-1): its type, locale, currency, style and resolved blocks. PDF bytes are not deterministic, so bytes
/// never enter it. The encoding is specified rather than borrowed from a serializer, whose escaping varies by runtime,
/// so another implementation reproduces it and a stored hash never has to migrate. Each field is its UTF-8 length in
/// decimal, a colon, its UTF-8 bytes and a comma; a null field is <c>~,</c>. The fields are type, locale, currency,
/// brand name and logo reference, then each block's id, page variant and content, in order.
/// </summary>
public static class DocumentContentHash
{
    /// <summary>Returns <c>sha256:</c> and 64 lowercase hex digits over the document's semantic content.</summary>
    public static string Of(RenderedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var buffer = new MemoryStream();
        foreach (var field in new[] { document.DocumentType, document.LocaleTag, document.CurrencyCode, document.Style?.BrandName, document.Style?.LogoAssetRef }
            .Concat(document.Blocks.SelectMany(block => new[] { block.Id, block.PageVariant, block.Content })))
        {
            var bytes = field is null ? "~,"u8.ToArray() : Framed(field);
            buffer.Write(bytes);
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static byte[] Framed(string field)
    {
        var utf8 = Encoding.UTF8.GetBytes(field);
        return [.. Encoding.ASCII.GetBytes(utf8.Length.ToString(CultureInfo.InvariantCulture) + ":"), .. utf8, (byte)','];
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
