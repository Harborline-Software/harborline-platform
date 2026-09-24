using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Environments;

namespace Harborline.Foundation.RuleEngine.Functions;

/// <summary>
/// A structurally discriminated function reference (DES-0018 <c>rules-ck-29</c>): exactly
/// <see cref="BuiltInFunction"/> or <see cref="PackageFunction"/>. The canonical form and the digest
/// carry the discriminant and the structured fields; <see cref="Display"/> is for people only and is
/// never parsed or hashed.
/// </summary>
public abstract record FunctionReference
{
    private protected FunctionReference() { }

    /// <summary>Sorted-key canonical JSON carrying the <c>kind</c> discriminant.</summary>
    public string CanonicalJson => Canonical().ToJsonString(BorrowerEnvironmentAdmission.CanonicalOptions);

    /// <summary>Lower-case hex SHA-256 of <see cref="CanonicalJson"/>.</summary>
    public string Digest => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson)));

    /// <summary>Human-readable form. Not a wire form: nothing parses or hashes it.</summary>
    public abstract string Display { get; }

    internal abstract JsonObject Canonical();
}

/// <summary>
/// The built-in arm. It has no public constructor: the only instances are the ones
/// <see cref="BuiltInFunctionRegister"/> holds, so package data cannot construct or shadow one.
/// </summary>
public sealed record BuiltInFunction : FunctionReference
{
    internal BuiltInFunction(string key) => Key = key;

    /// <summary>The register key, e.g. <c>money.add</c>.</summary>
    public string Key { get; }

    /// <inheritdoc />
    public override string Display => Key;

    internal override JsonObject Canonical() => new() { ["key"] = Key, ["kind"] = "builtin" };
}

/// <summary>
/// The package-provider arm. Modelled so the discriminant is real; R1 registers and executes no
/// package function (owner ruling 2026-09-21, T-685 owns the provider).
/// </summary>
public sealed record PackageFunction : FunctionReference
{
    /// <summary>Creates a package reference; <c>:</c> and empty values refuse.</summary>
    public PackageFunction(string packageId, string functionKey)
    {
        PackageId = FunctionReferenceCodec.Field(packageId);
        FunctionKey = FunctionReferenceCodec.Field(functionKey);
    }

    /// <summary>The exporting package.</summary>
    public string PackageId { get; }

    /// <summary>The function key inside that package.</summary>
    public string FunctionKey { get; }

    /// <inheritdoc />
    public override string Display => PackageId + "::" + FunctionKey;

    internal override JsonObject Canonical() => new() { ["functionKey"] = FunctionKey, ["kind"] = "package", ["packageId"] = PackageId };
}

/// <summary>A refused function reference, by stable code.</summary>
public sealed class FunctionReferenceException(string code, string message) : Exception(message)
{
    /// <summary>The stable refusal code.</summary>
    public string Code { get; } = code;
}

/// <summary>Reads the structured wire form of a <see cref="FunctionReference"/>.</summary>
public static class FunctionReferenceCodec
{
    /// <summary>A package payload tried to name the built-in arm.</summary>
    public const string BuiltInFromPackage = "rule.function.builtin_from_package";

    /// <summary>A structured field contains <c>:</c> or is empty.</summary>
    public const string ColonForbidden = "rule.function.field_invalid";

    /// <summary>The value is not a structured reference.</summary>
    public const string MalformedReference = "rule.function.malformed";

    /// <summary>The built-in key is not in the register.</summary>
    public const string UnknownBuiltIn = "rule.function.unknown_builtin";

    /// <summary>
    /// Reads a reference supplied by package data. Only the package arm can come out: a payload
    /// naming <c>builtin</c> refuses, whatever key it carries.
    /// </summary>
    public static PackageFunction ReadPackagePayload(JsonNode? node)
    {
        var obj = node as JsonObject ?? throw Malformed();
        if (Text(obj, "kind") == "builtin")
            throw new FunctionReferenceException(BuiltInFromPackage, "package data cannot construct a built-in function reference");
        return Read(obj) as PackageFunction ?? throw Malformed();
    }

    /// <summary>Reads a platform-authored reference. A built-in resolves only through the register.</summary>
    public static FunctionReference Read(JsonNode? node)
    {
        var obj = node as JsonObject ?? throw Malformed();
        return Text(obj, "kind") switch
        {
            "builtin" when obj.Count == 2 => BuiltInFunctionRegister.TryResolve(Text(obj, "key") ?? "", out var function)
                ? function.Reference
                : throw new FunctionReferenceException(UnknownBuiltIn, "the built-in key is not registered"),
            "package" when obj.Count == 3 => new PackageFunction(Text(obj, "packageId") ?? "", Text(obj, "functionKey") ?? ""),
            _ => throw Malformed(),
        };
    }

    internal static string Field(string value) => string.IsNullOrEmpty(value) || value.Contains(':')
        ? throw new FunctionReferenceException(ColonForbidden, "a function reference field must be non-empty and must not contain ':'")
        : value;

    private static string? Text(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var value) && value is JsonValue text && text.TryGetValue<string>(out var s) ? s : null;

    private static FunctionReferenceException Malformed() => new(MalformedReference, "a function reference must be the structured kind form");
}
