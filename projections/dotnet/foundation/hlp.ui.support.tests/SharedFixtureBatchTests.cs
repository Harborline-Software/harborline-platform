using System.Globalization;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

[CollectionDefinition("Shared fixture environment", DisableParallelization = true)]
public sealed class SharedFixtureEnvironmentCollection { }

[Collection("Shared fixture environment")]
public sealed class SharedFixtureBatchTests
{
    private int instanceRuns;

    [Theory]
    [InlineData(null, "(native)")]
    [InlineData("", "(native)")]
    [InlineData(" \t\r\n", "(native)")]
    [InlineData("{\"id\":\"singleton\"}", "singleton")]
    public void SingletonFallbackNormalizesBlankFixturesAndRetainsValidIdentity(string? raw, string expectedId)
    {
        var previousBatch = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_BATCH");
        var previousFixture = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        try
        {
            Environment.SetEnvironmentVariable("HARBORLINE_CONFORMANCE_BATCH", null);
            Environment.SetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE", raw);
            var row = SharedFixtureBatch.Cases("unused-for-singleton").Single();
            Assert.Equal(expectedId, row[0]);
            Assert.Equal(expectedId == "(native)" ? null : raw, row[1]);
            var called = false;
            SharedFixtureBatch.Run((string)row[0]!, (string?)row[1], () => called = true);
            Assert.True(called);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HARBORLINE_CONFORMANCE_BATCH", previousBatch);
            Environment.SetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE", previousFixture);
        }
    }

    [Fact]
    public void CaseScopePreservesCallerCultureAndNeverWritesTheFixtureToTheProcessEnvironment()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var processFixture = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        SharedFixtureBatch.Run("culture", "{\"id\":\"culture\"}", () =>
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");
            Assert.Equal(processFixture, Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"));
            Assert.Equal("{\"id\":\"culture\"}", SharedFixtureBatch.Current);
        });
        Assert.Equal(culture, CultureInfo.CurrentCulture);
        Assert.Equal(uiCulture, CultureInfo.CurrentUICulture);
        Assert.Equal(processFixture, Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"));
    }

    [Theory]
    [InlineData("first")]
    [InlineData("second")]
    public void TheoryRowsStartWithFreshTestInstanceState(string id)
    {
        Assert.Equal(0, instanceRuns);
        SharedFixtureBatch.Run(id, "{\"id\":\"" + id + "\"}", () => instanceRuns++);
        Assert.Equal(1, instanceRuns);
    }

    [Fact]
    public void NestedCasesRestoreTheirFixtureAfterSuccessAndFailure()
    {
        var initial = SharedFixtureBatch.Current;
        SharedFixtureBatch.Run("outer", "{\"id\":\"outer\"}", () =>
        {
            Assert.Equal("{\"id\":\"outer\"}", SharedFixtureBatch.Current);
            Assert.Throws<InvalidOperationException>(() => SharedFixtureBatch.Run("inner", "{\"id\":\"inner\"}", () =>
            {
                Assert.Equal("{\"id\":\"inner\"}", SharedFixtureBatch.Current);
                throw new InvalidOperationException("planted assertion failure");
            }));
            Assert.Equal("{\"id\":\"outer\"}", SharedFixtureBatch.Current);
        });
        Assert.Equal(initial, SharedFixtureBatch.Current);
    }

    [Fact]
    public async Task ConcurrentAsyncCasesCannotReadEachOthersFixture()
    {
        var initial = SharedFixtureBatch.Current;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        async Task Check(string id, string raw)
        {
            await SharedFixtureBatch.RunAsync(id, raw, async () =>
            {
                if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
                await bothStarted.Task;
                Assert.Equal(raw, SharedFixtureBatch.Current);
            });
        }
        await Task.WhenAll(Check("left", "{\"id\":\"left\"}"), Check("right", "{\"id\":\"right\"}"));
        Assert.Equal(initial, SharedFixtureBatch.Current);
    }

    [Fact]
    public void MismatchedCaseIdentityFailsBeforeTheAssertionRuns()
    {
        var called = false;
        Assert.Throws<InvalidOperationException>(() => SharedFixtureBatch.Run("expected", "{\"id\":\"different\"}", () => called = true));
        Assert.False(called);
    }
}
