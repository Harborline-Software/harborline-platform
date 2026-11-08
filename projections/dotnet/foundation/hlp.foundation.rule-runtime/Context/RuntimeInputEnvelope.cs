using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Foundation.RuleEngine.Context;

/// <summary>
/// Finite JSON-text host-capture envelope for runtime-owned evaluation data. Limits
/// cover UTF-8 source bytes, JSON nesting, and every JSON value (containers and
/// scalars); authored-expression limits are separate. The text must also be I-JSON in two respects
/// (T-1143, the owner's ruling of 2026-11-07): no lone surrogate, raw or escaped, in a member name or a
/// string (RFC 7493 §2.1; RFC 8259 §8.2 calls such text unpredictable), and no duplicate member name after
/// unescaping (RFC 7493 §2.3). Both refuse here, before any conversion can rewrite the text and before a
/// later read can throw, with the same reason the TypeScript tier gives.
/// </summary>
internal static class RuntimeInputEnvelope
{
    internal const int MaxUtf8Bytes = 262_144;
    internal const int MaxDepth = 64;
    internal const int MaxNodes = 5_000;
    internal const string LoneSurrogate = "Rule context is not I-JSON: it holds a lone surrogate (RFC 7493 §2.1).";
    internal const string DuplicateMemberName = "Rule context is not I-JSON: it repeats a member name (RFC 7493 §2.3).";

    // Encoding.UTF8 replaces a lone surrogate with U+FFFD; this one throws instead, so nothing is rewritten.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static JsonNode? Parse(string jsonText, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(jsonText);
        // UTF-8 never uses fewer bytes than UTF-16 code units. Reject this cheap
        // upper bound before scanning or allocating a byte buffer for hostile text.
        if (jsonText.Length > MaxUtf8Bytes)
            throw new ArgumentException("Rule context exceeds the UTF-8 byte envelope.", parameterName);
        if (Utf8ByteCount(jsonText, parameterName) > MaxUtf8Bytes)
            throw new ArgumentException("Rule context exceeds the UTF-8 byte envelope.", parameterName);
        var utf8 = StrictUtf8.GetBytes(jsonText);

        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = MaxDepth });
            var nodes = 0;
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray or JsonTokenType.String
                    or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null)
                {
                    if (++nodes > MaxNodes)
                        throw new ArgumentException("Rule context exceeds the node envelope.", parameterName);
                }

                // An escaped lone surrogate is valid JSON syntax, so the reader accepts it; unescaping it throws, and
                // that is the refusal (Utf8JsonReader.GetString: "invalid UTF-16 surrogates").
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && reader.ValueIsEscaped)
                {
                    try { _ = reader.GetString(); }
                    catch (InvalidOperationException exception) { throw new ArgumentException(LoneSurrogate, parameterName, exception); }
                }
            }

            // A JSON null is a valid captured scalar for reactive field updates. A repeated member name, compared
            // after unescaping, fails the parse here rather than the first read of its object. The reader pass above has
            // already proved the syntax and the depth, so a repeated name is the only failure left at this point.
            try
            {
                return JsonNode.Parse(utf8, documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth, AllowDuplicateProperties = false });
            }
            catch (JsonException exception)
            {
                throw new ArgumentException(DuplicateMemberName, parameterName, exception);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Rule context is not valid bounded JSON.", parameterName, exception);
        }
    }

    internal static void ValidateMemberName(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaxUtf8Bytes || Utf8ByteCount(value, parameterName) > MaxUtf8Bytes)
            throw new ArgumentException("Rule context member name exceeds the UTF-8 byte envelope.", parameterName);
    }

    private static int Utf8ByteCount(string value, string parameterName)
    {
        try { return StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException exception) { throw new ArgumentException(LoneSurrogate, parameterName, exception); }
    }
}
