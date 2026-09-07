using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0135 A0 — the shared <see cref="HumanApprovalHandlerBase.ReadHumanAction"/> human-action parser that
/// retired the three byte-identical copies (invoice approval + the two grant handlers). One tested
/// implementation of the security-sensitive human-action funnel; the per-handler decision logic stays in the
/// handlers (proven by their own tests staying green).
/// </summary>
public sealed class HumanApprovalHandlerBaseTests
{
    [Theory]
    [InlineData("{\"decision\":\"approve\"}", "approve")]
    [InlineData("{\"decision\":\"reject\"}", "reject")]
    [InlineData("{\"decision\":\"send-back\"}", "send-back")]
    [InlineData("{\"decision\":\"confirm\",\"note\":\"extra fields ignored\"}", "confirm")]
    public void ReadsTheDecisionVerb(string payload, string expected)
        => Assert.Equal(expected, HumanApprovalHandlerBase.ReadHumanAction(payload, "approve"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NullOrBlankPayload_Throws(string? payload)
        => Assert.Throws<InvalidOperationException>(() => HumanApprovalHandlerBase.ReadHumanAction(payload!, "approve"));

    [Fact(DisplayName = "A0: a payload with no 'decision' field throws (the funnel rejects a malformed resume)")]
    public void MissingDecisionField_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => HumanApprovalHandlerBase.ReadHumanAction("{\"note\":\"no decision\"}", "approve"));

    [Fact(DisplayName = "A0: a non-string 'decision' (e.g. a number) throws — the funnel requires a string verb")]
    public void NonStringDecision_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => HumanApprovalHandlerBase.ReadHumanAction("{\"decision\":42}", "approve"));

    [Fact(DisplayName = "A0: an empty-string 'decision' throws (no silent empty verb reaching a handler switch)")]
    public void EmptyStringDecision_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => HumanApprovalHandlerBase.ReadHumanAction("{\"decision\":\"\"}", "approve"));

    [Fact(DisplayName = "A0: the step name is woven into the error message (per-handler diagnostics preserved)")]
    public void ErrorMessage_NamesTheStep()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => HumanApprovalHandlerBase.ReadHumanAction("{}", "confirm"));
        Assert.Contains("confirm", ex.Message);
    }
}
