using System.Text;

namespace Harborline.Blocks.RelativeChains;

/// <summary>Canonical r2 encoder for opaque versioned occurrence references.</summary>
public static class RelativeChainReferenceCodec
{
    /// <summary>The maximum UTF-8 byte length of one input reference component.</summary>
    public const int MaximumReferenceUtf8Bytes = 256;

    /// <summary>Encodes a full occurrence identity as an injective <c>rc1</c> reference.</summary>
    /// <param name="id">The identity to encode.</param>
    /// <returns>The opaque canonical reference.</returns>
    public static string Encode(RelativeChainOccurrenceId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.OccurrenceRevision is < 1) throw new ArgumentOutOfRangeException(nameof(id));
        return $"rc1/{Escape(id.ChainInstanceId)}/{Escape(id.NodeId)}/{id.DueDate:yyyy-MM-dd}/r{id.OccurrenceRevision}";
    }

    /// <summary>Percent-escapes one bounded UTF-8 component using uppercase hexadecimal.</summary>
    /// <param name="value">The component to encode.</param>
    /// <returns>The canonical escaped component.</returns>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length is < 1 or > MaximumReferenceUtf8Bytes) throw new ArgumentOutOfRangeException(nameof(value));
        var builder = new StringBuilder(bytes.Length);
        foreach (var valueByte in bytes)
        {
            if ((valueByte >= 'A' && valueByte <= 'Z') || (valueByte >= 'a' && valueByte <= 'z') || (valueByte >= '0' && valueByte <= '9') || valueByte is (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
                builder.Append((char)valueByte);
            else
                builder.Append('%').Append(valueByte.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}
