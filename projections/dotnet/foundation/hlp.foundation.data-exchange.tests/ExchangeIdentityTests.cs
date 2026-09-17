using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class ExchangeIdentityTests
{
    private static readonly BatchIdentityInputs Inputs = new(
        "tenant-a",
        "exchange.customers",
        "1.2.0",
        "mapping.customers",
        "1.0.0",
        "sha256:map",
        "erpnext",
        "4.1.0",
        "sha256:source",
        "rows:1-2",
        "records.customer/v1");

    [Fact]
    public void Batch_identity_is_a_stable_versioned_digest_of_semantic_inputs()
    {
        var identity = ExchangeIdentity.DeriveBatch(Inputs);

        Assert.Equal(
            "hl-batch-v1:279cd91dfd2a40e77ecf8b3b8d7ef17fd5044f4201946f0f7816631f466d2d9b",
            identity.Value);
        Assert.Equal(identity, ExchangeIdentity.DeriveBatch(Inputs));
    }

    [Fact]
    public void Effect_identity_is_stable_but_attempt_identity_is_not_semantic()
    {
        var batch = ExchangeIdentity.DeriveBatch(Inputs);

        var effect = ExchangeIdentity.DeriveEffect(
            batch,
            "records.customer/v1",
            "customer-42",
            "source-v3",
            "primary");
        var replayed = ExchangeIdentity.DeriveEffect(
            batch,
            "records.customer/v1",
            "customer-42",
            "source-v3",
            "primary");

        Assert.Equal(effect, replayed);
        Assert.StartsWith("hl-effect-v1:", effect.Value, StringComparison.Ordinal);
        Assert.NotEqual(AttemptId.New(), AttemptId.New());
    }

    [Fact]
    public void Proposal_affecting_change_creates_a_different_batch()
    {
        var changedSource = Inputs with { SourceFingerprint = "sha256:changed" };

        Assert.NotEqual(
            ExchangeIdentity.DeriveBatch(Inputs),
            ExchangeIdentity.DeriveBatch(changedSource));
    }
}
