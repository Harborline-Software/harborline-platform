using Harborline.Blocks.Scheduling;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleChainReferenceCodecProofTests
{
    private static RelativeChainOccurrenceId Id(string chain = "a/b", string node = "n%é", int revision = 2)
        => new(chain, node, new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), revision);

    // Literal oracles: frozen Scheduling interface canonicalReferences occurrenceFormat,
    // escaping and limits; chain-reference-codec-proof acceptance 1–2. No codec-derived expectations.
    [Fact(DisplayName = "scheduling-run-2 / scheduling-eng-5: opaque-reference producer and identity preserve escaped coordinates and revision")]
    public void EncodeAndIdentityToStringMatchFrozenLiteral()
    {
        const string expected = "rc1/a%2Fb/n%25%C3%A9/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r2";
        Assert.Equal(expected, RelativeChainReferenceCodec.Encode(Id()));
        Assert.Equal(expected, Id().ToString());
    }

    [Theory(DisplayName = "scheduling-run-2 / scheduling-eng-5: opaque-reference components use uppercase UTF-8 percent escapes")]
    [InlineData("AZaz09-._~", "AZaz09-._~")]
    [InlineData("/", "%2F")]
    [InlineData("%", "%25")]
    [InlineData(" ", "%20")]
    [InlineData("+", "%2B")]
    [InlineData("é", "%C3%A9")]
    public void EscapeMatchesIndependentLiteral(string input, string expected)
        => Assert.Equal(expected, RelativeChainReferenceCodec.Escape(input));

    [Fact(DisplayName = "scheduling-run-2 / scheduling-eng-5: opaque-reference component boundaries and positive revisions remain distinct")]
    public void CompleteLiteralsKeepBoundariesAndRevisionsDistinct()
    {
        var first = RelativeChainReferenceCodec.Encode(Id("a/b", "c", 1));
        var second = RelativeChainReferenceCodec.Encode(Id("a", "b/c", 1));
        var revisedFirst = RelativeChainReferenceCodec.Encode(Id("a/b", "c", 2));
        var revisedSecond = RelativeChainReferenceCodec.Encode(Id("a", "b/c", 2));
        Assert.Equal("rc1/a%2Fb/c/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r1", first);
        Assert.Equal("rc1/a/b%2Fc/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r1", second);
        Assert.Equal("rc1/a%2Fb/c/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r2", revisedFirst);
        Assert.Equal("rc1/a/b%2Fc/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r2", revisedSecond);
        Assert.Equal(4, new HashSet<string> { first, second, revisedFirst, revisedSecond }.Count);
    }

    [Fact(DisplayName = "scheduling-run-2 / scheduling-eng-5: opaque-reference null identities and null or empty components refuse")]
    public void NullIdentityAndInvalidComponentsRefuse()
    {
        Assert.Throws<ArgumentNullException>(() => RelativeChainReferenceCodec.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => RelativeChainReferenceCodec.Escape(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => RelativeChainReferenceCodec.Escape(""));
        Assert.Throws<ArgumentNullException>(() => RelativeChainReferenceCodec.Encode(Id(chain: null!)));
        Assert.Throws<ArgumentNullException>(() => RelativeChainReferenceCodec.Encode(Id(node: null!)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RelativeChainReferenceCodec.Encode(Id(chain: "")));
        Assert.Throws<ArgumentOutOfRangeException>(() => RelativeChainReferenceCodec.Encode(Id(node: "")));
    }

    [Theory(DisplayName = "scheduling-run-2 / scheduling-eng-5: opaque-reference non-positive revisions refuse and corrected revision one succeeds")]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveRevisionRefusesAndCorrectionSucceeds(int revision)
    {
        var invalid = Id(revision: revision);
        Assert.Throws<ArgumentOutOfRangeException>(() => RelativeChainReferenceCodec.Encode(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.ToString());
        var corrected = invalid with { OccurrenceRevision = 1 };
        Assert.Equal("rc1/a%2Fb/n%25%C3%A9/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r1", RelativeChainReferenceCodec.Encode(corrected));
        Assert.Equal("rc1/a%2Fb/n%25%C3%A9/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r1", corrected.ToString());
    }
}
