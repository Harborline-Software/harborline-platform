using Harborline.Foundation.NotificationDelivery;
using Xunit;

namespace Harborline.Foundation.NotificationDelivery.Tests;

[CollectionDefinition("Delivery channel registry mutation isolation", DisableParallelization = true)]
public sealed class DeliveryChannelRegistryIsolationCollection
{
}

[Collection("Delivery channel registry mutation isolation")]
public sealed class DeliveryChannelRegistryIsolationTests
{
    private const string UndeclaredName = "undeclared-reference";

    // DES-0055 ck-1/ck-2: callers cannot turn the inbox into a secret-needing channel.
    [Theory]
    [InlineData("set-add")]
    [InlineData("set-remove")]
    [InlineData("set-clear")]
    [InlineData("set-except")]
    [InlineData("set-intersect")]
    [InlineData("set-symmetric-except")]
    [InlineData("set-union")]
    [InlineData("collection-add")]
    [InlineData("collection-remove")]
    [InlineData("collection-clear")]
    public void Secret_declarations_stay_closed_after_supported_collection_mutations(string route)
    {
        var declarations = DeliveryChannels.Require(DeliveryChannelKey.Inbox).RequiredSecretReferenceNames;
        Assert.Empty(declarations);
        try
        {
            AttemptSupportedMutation(declarations, route);
            AssertSecretReferencesRefused();
            AssertCanonicalRegistry();
            AssertEmptyBindingsRemainLawful();
        }
        finally
        {
            RestoreEmptyDeclaration(declarations);
        }
    }

    // DES-0055 ruling 7: this observes the declaration, not retry execution.
    [Theory]
    [InlineData("set-add")]
    [InlineData("set-remove")]
    [InlineData("set-clear")]
    [InlineData("set-except")]
    [InlineData("set-intersect")]
    [InlineData("set-symmetric-except")]
    [InlineData("set-union")]
    [InlineData("collection-add")]
    [InlineData("collection-remove")]
    [InlineData("collection-clear")]
    public void Retry_declarations_stay_empty_after_supported_collection_mutations(string route)
    {
        var declarations = DeliveryChannels.Require(DeliveryChannelKey.Inbox).AllowedRetryProfiles;
        Assert.Empty(declarations);
        try
        {
            AttemptSupportedMutation(declarations, route);
            AssertCanonicalRegistry();
            AssertSecretReferencesRefused();
            AssertEmptyBindingsRemainLawful();
        }
        finally
        {
            RestoreEmptyDeclaration(declarations);
        }
    }

    private static void AssertCanonicalRegistry()
    {
        var channel = DeliveryChannels.Require(DeliveryChannelKey.Inbox);
        Assert.Equal("inbox", channel.Key.Value);
        Assert.Empty(channel.RequiredSecretReferenceNames);
        Assert.Empty(channel.AllowedRetryProfiles);
        Assert.Equal("OnNodeFull", channel.ContentClass.ToString());
        Assert.Equal("LinkOnly", DeliveryChannels.ExternalDefaultContentClass.ToString());

        var unknown = Assert.Throws<NotificationDeliveryRefusedException>(() => DeliveryChannels.Require(default));
        Assert.Equal("notification_delivery.channel_unknown", unknown.Code);
        Assert.Equal("Channel '' is not registered.", unknown.Message);
    }

    private static void AssertSecretReferencesRefused()
    {
        // Exercise both constructors before asserting either outcome.
        var enabled = Record.Exception(() => new ChannelBinding(
            "tenant-a", DeliveryChannelKey.Inbox, true, "notification-sender", [UndeclaredName]));
        var disabled = Record.Exception(() => new ChannelBinding(
            "tenant-a", DeliveryChannelKey.Inbox, false, null, [UndeclaredName]));

        foreach (var observed in new[] { enabled, disabled })
        {
            var refusal = Assert.IsType<NotificationDeliveryRefusedException>(observed);
            Assert.Equal("notification_delivery.secret_reference_not_allowed", refusal.Code);
            Assert.Equal("Channel 'inbox' declares no secret references.", refusal.Message);
        }
    }

    private static void AssertEmptyBindingsRemainLawful()
    {
        var enabled = new ChannelBinding(
            "tenant-a", DeliveryChannelKey.Inbox, true, "notification-sender", []);
        Assert.Equal("tenant-a", enabled.Tenant);
        Assert.Equal("inbox", enabled.Channel.Value);
        Assert.True(enabled.Enabled);
        Assert.Equal("notification-sender", enabled.SenderIdentity);
        Assert.Empty(enabled.SecretReferenceIds);

        var disabled = new ChannelBinding(
            "tenant-a", DeliveryChannelKey.Inbox, false, null, []);
        Assert.Equal("tenant-a", disabled.Tenant);
        Assert.Equal("inbox", disabled.Channel.Value);
        Assert.False(disabled.Enabled);
        Assert.Null(disabled.SenderIdentity);
        Assert.Empty(disabled.SecretReferenceIds);
    }

    private static void AttemptSupportedMutation(IReadOnlySet<string> declarations, string route)
    {
        // Interface absence is safe; a supported mutator must actually be attempted.
        switch (route)
        {
            case "set-add":
                if (declarations is ISet<string> addSet)
                    Attempt(() => addSet.Add(UndeclaredName));
                break;
            case "set-remove":
                if (declarations is ISet<string> removeSet)
                    Attempt(() => removeSet.Remove(UndeclaredName));
                break;
            case "set-clear":
                if (declarations is ISet<string> clearSet)
                    Attempt(clearSet.Clear);
                break;
            case "set-except":
                if (declarations is ISet<string> exceptSet)
                    Attempt(() => exceptSet.ExceptWith([UndeclaredName]));
                break;
            case "set-intersect":
                if (declarations is ISet<string> intersectSet)
                    Attempt(() => intersectSet.IntersectWith([UndeclaredName]));
                break;
            case "set-symmetric-except":
                if (declarations is ISet<string> symmetricSet)
                    Attempt(() => symmetricSet.SymmetricExceptWith([UndeclaredName]));
                break;
            case "set-union":
                if (declarations is ISet<string> unionSet)
                    Attempt(() => unionSet.UnionWith([UndeclaredName]));
                break;
            case "collection-add":
                if (declarations is ICollection<string> addCollection)
                    Attempt(() => addCollection.Add(UndeclaredName));
                break;
            case "collection-remove":
                if (declarations is ICollection<string> removeCollection)
                    Attempt(() => removeCollection.Remove(UndeclaredName));
                break;
            case "collection-clear":
                if (declarations is ICollection<string> clearCollection)
                    Attempt(clearCollection.Clear);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(route));
        }
    }

    private static void Attempt(Action mutation)
    {
        try
        {
            mutation();
        }
        catch (NotSupportedException)
        {
            // A read-only implementation may reject its exposed mutation interface.
        }
    }

    private static void RestoreEmptyDeclaration(IReadOnlySet<string> declarations)
    {
        if (declarations.Count == 0)
            return;

        // A baseline or mutant that accepted a mutation must not poison other tests.
        if (declarations is ISet<string> set)
            Attempt(set.Clear);
        if (declarations.Count != 0 && declarations is ICollection<string> collection)
        {
            Attempt(collection.Clear);
            foreach (var value in declarations.ToArray())
                Attempt(() => collection.Remove(value));
        }
        Assert.Empty(declarations);
    }
}
