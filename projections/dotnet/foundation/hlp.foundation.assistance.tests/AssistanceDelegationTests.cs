using System.Text;
using System.Text.Json;
using Harborline.Foundation.Assistance;
using Xunit;

namespace Harborline.Foundation.Assistance.Tests;

public sealed class AssistanceDelegationTests
{
    [Fact(DisplayName = "pilot-ck-15 pilot-ck-18: canonical bytes and digest; oracle: RFC 8785 fixture bytes and independent shasum literals")]
    public void Proposal_canonical_bytes_and_digest_match_the_shared_oracle()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "delegation-cases.json")));
        foreach (var item in fixture.RootElement.GetProperty("envelopes").EnumerateArray())
        {
            var proposal = AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(item.GetProperty("value").GetRawText());
            var canonical = AssistanceDelegationJson.SerializeCanonical(proposal);
            Assert.Equal(Encoding.UTF8.GetBytes(item.GetProperty("canonical").GetString()!), canonical);
            Assert.Equal(item.GetProperty("digest").GetString(), AssistanceProposalDigest.Compute(proposal));
            Assert.Equal(canonical, AssistanceDelegationJson.SerializeCanonical(AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(Encoding.UTF8.GetString(canonical))));
        }
    }
}
