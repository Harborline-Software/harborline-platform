using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Foundation.RuleEngine.Context;

/// <summary>
/// Finite JSON-text host-capture envelope for runtime-owned evaluation data. Limits
/// cover UTF-8 source bytes, JSON nesting, and every JSON value (containers and
/// scalars); authored-expression limits are separate.
/// </summary>
internal static class RuntimeInputEnvelope
{
    internal const int MaxUtf8Bytes = 262_144;
    internal const int MaxDepth = 64;
    internal const int MaxNodes = 5_000;

    internal static JsonNode? Parse(string jsonText, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(jsonText);
        // UTF-8 never uses fewer bytes than UTF-16 code units. Reject this cheap
        // upper bound before scanning or allocating a byte buffer for hostile text.
        if (jsonText.Length > MaxUtf8Bytes)
            throw new ArgumentException("Rule context exceeds the UTF-8 byte envelope.", parameterName);
        if (Encoding.UTF8.GetByteCount(jsonText) > MaxUtf8Bytes)
            throw new ArgumentException("Rule context exceeds the UTF-8 byte envelope.", parameterName);
        var utf8 = Encoding.UTF8.GetBytes(jsonText);

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
            }

            // A JSON null is a valid captured scalar for reactive field updates.
            return JsonNode.Parse(utf8, documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Rule context is not valid bounded JSON.", parameterName, exception);
        }
    }

    internal static void ValidateMemberName(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaxUtf8Bytes || Encoding.UTF8.GetByteCount(value) > MaxUtf8Bytes)
            throw new ArgumentException("Rule context member name exceeds the UTF-8 byte envelope.", parameterName);
    }
}
