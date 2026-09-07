namespace Harborline.Kernel.SchemaValidation;

/// <summary>A content-derived identifier for a registered JSON Schema document.</summary>
public readonly record struct SchemaId
{
    /// <summary>Creates a non-empty schema identifier.</summary>
    public SchemaId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Schema id must not be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the wire value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>A registered, content-addressed JSON Schema document.</summary>
public sealed record Schema(
    SchemaId Id,
    string JsonSchemaText,
    IReadOnlyList<SchemaId> ParentSchemas,
    IReadOnlyList<string> Tags,
    string ContentAddress);

/// <summary>The result of validating one payload against one registered schema.</summary>
public sealed record SchemaValidationResult(
    bool IsValid,
    IReadOnlyList<SchemaValidationError> Errors);

/// <summary>A stable, localizable validation failure addressed by RFC 6901 JSON Pointer.</summary>
public sealed record SchemaValidationError(
    string JsonPointer,
    string Message,
    string? Code = null,
    IReadOnlyDictionary<string, string>? Params = null);

/// <summary>Registers and validates the JSON Schemas used by Dynamic Forms.</summary>
public interface ISchemaRegistry
{
    /// <summary>Gets a registered schema or null when it is unknown.</summary>
    ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default);

    /// <summary>Registers a draft 2020-12 schema idempotently by canonical content.</summary>
    ValueTask<Schema> RegisterAsync(
        string jsonSchemaText,
        IReadOnlyList<SchemaId>? parents = null,
        IReadOnlyList<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>Validates UTF-8 JSON against a registered schema.</summary>
    ValueTask<SchemaValidationResult> ValidateAsync(
        SchemaId id,
        ReadOnlyMemory<byte> documentBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Lists registered schemas, optionally filtered by an exact tag.</summary>
    IAsyncEnumerable<Schema> ListAsync(
        string? tagFilter = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a schema cannot be parsed safely as draft 2020-12 JSON Schema.</summary>
public sealed class InvalidSchemaException : Exception
{
    /// <summary>Creates an invalid-schema failure.</summary>
    public InvalidSchemaException(string message) : base(message) { }

    /// <summary>Creates an invalid-schema failure with its parser cause.</summary>
    public InvalidSchemaException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Thrown when validation names a schema that is not registered.</summary>
public sealed class SchemaNotFoundException : Exception
{
    /// <summary>Creates an unknown-schema failure.</summary>
    public SchemaNotFoundException(string message) : base(message) { }
}

/// <summary>Resource bounds applied before or during schema evaluation.</summary>
public sealed class SchemaRegistryOptions
{
    /// <summary>The default maximum canonical schema size.</summary>
    public const int DefaultMaxSchemaBytes = 256 * 1024;
    /// <summary>The default maximum schema JSON nesting depth.</summary>
    public const int DefaultMaxNestingDepth = 64;

    /// <summary>Gets the maximum canonical schema size.</summary>
    public int MaxSchemaBytes { get; init; } = DefaultMaxSchemaBytes;

    /// <summary>Gets the maximum schema JSON nesting depth.</summary>
    public int MaxNestingDepth { get; init; } = DefaultMaxNestingDepth;

    /// <summary>Gets the maximum duration of one schema pattern match.</summary>
    public TimeSpan PatternMatchTimeout { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Gets the default resource bounds.</summary>
    public static SchemaRegistryOptions Default { get; } = new();

    internal void Validate()
    {
        if (MaxSchemaBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSchemaBytes));
        if (MaxNestingDepth <= 0) throw new ArgumentOutOfRangeException(nameof(MaxNestingDepth));
        if (PatternMatchTimeout <= TimeSpan.Zero || PatternMatchTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(PatternMatchTimeout));
        }
    }
}
