using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Capabilities;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class FormCapabilityVerificationTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("forms-root-key-for-tests-only");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");

    [Fact]
    public async Task VerifyAsync_MalformedEncodingSignatureOrUnknownKey_DeniesWithoutSecretEcho()
    {
        var validCaveats = Caveats("action = read");
        var malformed = "bearer-secret%%%";
        var badSignature = FormMacaroonCodec.EncodeSigned(MacaroonFormCapabilityIssuer.DefaultLocation, Identifier(FormCapabilityAction.Read), validCaveats, "wrong-secret-key"u8);
        var unknownKey = FormMacaroonCodec.EncodeSigned("unknown-location-secret", Identifier(FormCapabilityAction.Read), validCaveats, Key);
        var verifier = new MacaroonFormCapabilityVerifier(new TestKeys(Key));

        var errors = new[]
        {
            await DeniedAsync(verifier, malformed),
            await DeniedAsync(verifier, badSignature),
            await DeniedAsync(verifier, unknownKey),
        };

        Assert.All(errors, error =>
        {
            Assert.Equal("form.engine.denied", error.Code);
            Assert.Equal("The form capability was denied.", error.Message);
            Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tenant-a", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", error.ToString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task VerifyAsync_MalformedUnknownMissingDuplicateOrEmptyCaveat_Denies()
    {
        var cases = new IReadOnlyList<string>[]
        {
            ["tenant tenant-a", "subject = alice", $"expires = {Now.AddMinutes(5):O}", "action = read"],
            ["tenant = tenant-a", "subject = alice", $"expires = {Now.AddMinutes(5):O}", "unknown = value", "action = read"],
            ["subject = alice", $"expires = {Now.AddMinutes(5):O}", "action = read"],
            ["tenant = tenant-a", "tenant = tenant-b", "subject = alice", $"expires = {Now.AddMinutes(5):O}", "action = read"],
            ["tenant = ", "subject = alice", $"expires = {Now.AddMinutes(5):O}", "action = read"],
            ["tenant = tenant-a", "subject = ", $"expires = {Now.AddMinutes(5):O}", "action = read"],
            ["tenant = tenant-a", "subject = alice", $"expires = {Now.AddMinutes(5):O}", "action = "],
            ["tenant = tenant-a", "subject = alice", $"expires = {Now.AddMinutes(5):O}", "role = "],
            ["tenant = tenant-a", "subject = alice", $"expires = {Now.AddMinutes(5):O}", "action = read", "action = write"],
        };
        var verifier = new MacaroonFormCapabilityVerifier(new TestKeys(Key));

        foreach (var caveats in cases)
        {
            var bearer = FormMacaroonCodec.EncodeSigned(MacaroonFormCapabilityIssuer.DefaultLocation, Identifier(FormCapabilityAction.Read), caveats, Key);
            var error = await DeniedAsync(verifier, bearer);
            Assert.Equal("form.engine.denied", error.Code);
        }
    }

    [Fact]
    public async Task VerifyAsync_UnknownActionInvalidExpiryOrExpiredCapability_Denies()
    {
        var cases = new[]
        {
            Caveats("action = delete"),
            new[] { "tenant = tenant-a", "subject = alice", "expires = yesterday", "action = read" },
            new[] { "tenant = tenant-a", "subject = alice", $"expires = {Now.AddTicks(-1):O}", "action = read" },
        };
        var verifier = new MacaroonFormCapabilityVerifier(new TestKeys(Key));

        foreach (var caveats in cases)
        {
            var bearer = FormMacaroonCodec.EncodeSigned(MacaroonFormCapabilityIssuer.DefaultLocation, Identifier(FormCapabilityAction.Read), caveats, Key);
            await DeniedAsync(verifier, bearer);
        }
    }

    [Fact]
    public async Task IssuerAndVerifier_PreserveClaimsAndExpiryBoundaryButDiscardBearerRoles()
    {
        var keys = new TestKeys(Key);
        var issuer = new MacaroonFormCapabilityIssuer(keys);
        var verifier = new MacaroonFormCapabilityVerifier(keys);
        var bearer = await issuer.IssueAsync(
            new TenantId("tenant-a"),
            "alice",
            ["bearer-admin", "bearer-admin"],
            [FormCapabilityAction.Read],
            Now.AddMinutes(5));

        var capability = await verifier.VerifyAsync(bearer, Now.AddMinutes(5));

        Assert.Equal(new TenantId("tenant-a"), capability.Tenant);
        Assert.Equal("alice", capability.Subject);
        Assert.Equal([FormCapabilityAction.Read], capability.Actions.ToArray());
        Assert.DoesNotContain(capability.GetType().GetProperties(), property => property.Name.Contains("Role", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PinnedSourceWireVector_PreservesWireAndHmacButLegacyIdentifierDenies()
    {
        var key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        const string bearer = "c3VuZmlzaC9mb3Jtcx4wMDExMjIzMzQ0NTU2Njc3ODg5OWFhYmJjY2RkZWVmZh50ZW5hbnQgPSB0ZW5hbnQ6YWNtZR5zdWJqZWN0ID0gdXNlcjphbGljZR5leHBpcmVzID0gMjAzMC0wMS0wMlQwMzowNDowNS4wMDAwMDAwKzAwOjAwHnJvbGUgPSByZWFkZXIeYWN0aW9uID0gcmVhZB_kobxyxkcErdCJV-f2-USYB-NtAN_itJuEPvH5Fhv2bg";
        var decoded = FormMacaroonCodec.Decode(bearer);
        var expected = FormMacaroonCodec.ComputeChain(key, decoded.Identifier, decoded.Caveats);
        var verifier = new MacaroonFormCapabilityVerifier(new TestKeys(key));

        Assert.Equal("sunfish/forms", decoded.Location);
        // The issuer default is pinned to the same wire location this captured vector carries;
        // every other test in this file spells it as the constant, so this line is what keeps the
        // constant honest against the wire.
        Assert.Equal(MacaroonFormCapabilityIssuer.DefaultLocation, decoded.Location);
        Assert.Equal("00112233445566778899aabbccddeeff", decoded.Identifier);
        Assert.True(System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, decoded.Signature));
        await DeniedAsync(verifier, bearer);
    }

    [Fact]
    public async Task VerifyAsync_ActionlessLegacyTokenAttenuatedWithWrite_Denies()
    {
        var original = FormMacaroonCodec.Decode(
            FormMacaroonCodec.EncodeSigned(MacaroonFormCapabilityIssuer.DefaultLocation, "legacy-source", Caveats(), Key));
        var appended = "action = write";
        var caveats = original.Caveats.Append(appended).ToArray();
        var signature = System.Security.Cryptography.HMACSHA256.HashData(original.Signature, Encoding.UTF8.GetBytes(appended));
        var bearer = FormMacaroonCodec.EncodeWithSignature(original.Location, original.Identifier, caveats, signature);
        var verifier = new MacaroonFormCapabilityVerifier(new TestKeys(Key));

        await DeniedAsync(verifier, bearer);
    }

    [Fact]
    public async Task IssueAsync_OversizedOrSeparatorBearingCapability_RejectsBeforeKeyLookup()
    {
        var keys = new CountingKeys(Key);
        var issuer = new MacaroonFormCapabilityIssuer(keys);

        await Assert.ThrowsAsync<ArgumentException>(async () => await issuer.IssueAsync(
            new TenantId("tenant-a"), "alice", Enumerable.Repeat("role", 61).ToArray(),
            [FormCapabilityAction.Read], Now.AddMinutes(5)));
        await Assert.ThrowsAsync<ArgumentException>(async () => await issuer.IssueAsync(
            new TenantId("tenant-a"), new string('a', 2049), [],
            [FormCapabilityAction.Read], Now.AddMinutes(5)));
        await Assert.ThrowsAsync<ArgumentException>(async () => await issuer.IssueAsync(
            new TenantId("tenant-a"), "alice", ["role\u001eadmin"],
            [FormCapabilityAction.Read], Now.AddMinutes(5)));

        Assert.Equal(0, keys.CallCount);
    }

    internal static IReadOnlyList<string> Caveats(params string[] additional) =>
        ["tenant = tenant-a", "subject = alice", $"expires = {Now.AddMinutes(5):O}", .. additional];

    private static string Identifier(FormCapabilityAction action) =>
        $"hlf1:{(action == FormCapabilityAction.Read ? "read" : "write")}:{Guid.NewGuid():N}";

    private static async Task<FormCapabilityDeniedException> DeniedAsync(
        IFormCapabilityVerifier verifier,
        string bearer) =>
        await Assert.ThrowsAsync<FormCapabilityDeniedException>(async () =>
            await verifier.VerifyAsync(bearer, Now));

    internal sealed class TestKeys(byte[] key) : IFormCapabilityRootKeyProvider
    {
        public ValueTask<ReadOnlyMemory<byte>?> GetRootKeyAsync(
            string location,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>?>(
                string.Equals(location, MacaroonFormCapabilityIssuer.DefaultLocation, StringComparison.Ordinal)
                    ? key.AsMemory()
                    : null);
    }

    private sealed class CountingKeys(byte[] key) : IFormCapabilityRootKeyProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>?> GetRootKeyAsync(
            string location,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult<ReadOnlyMemory<byte>?>(key.AsMemory());
        }
    }
}
