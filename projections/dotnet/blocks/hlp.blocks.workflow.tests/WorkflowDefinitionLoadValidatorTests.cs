using System;
using System.Collections.Generic;
using System.Text.Json;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0135 A1 R-1 (the D7-re-pin clause) + ADR 0143 R1-E (SC2 DoD item 4) — LOAD-time re-validation. A
/// definition admitted at persist under an OLDER capability registry must be RE-CHECKED when loaded for
/// execution; if a capability was later reclassified to CP (or an edit routed a CP edge around the
/// human-task), the persisted definition is now inadmissible and must fail-closed rather than execute.
/// </summary>
public sealed class WorkflowDefinitionLoadValidatorTests
{
    private const string Dispatch = "notify.dispatch";

    // A definition with an AUTONOMOUS (Schedule) trigger firing an action whose capability is `notify.dispatch`,
    // authored as AP. Admissible while the registry says `notify.dispatch` is AP; inadmissible once it is CP
    // (an AP-authored action whose capability derives CP ⇒ ClassificationMismatch; and a derived-CP action on
    // an autonomous edge with no human gate ⇒ CpReachableWithoutHumanTask).
    private static JsonElement AuthoredAutonomousApAction()
    {
        const string json = """
        {
          "initialState": "start",
          "mutability": "Locked",
          "states": [
            { "id": "start", "kind": "Normal" },
            { "id": "done", "kind": "Terminal" }
          ],
          "triggers": [
            { "id": "nightly", "kind": "Schedule", "rrule": "FREQ=DAILY" }
          ],
          "transitions": [
            { "id": "t1", "from": "start", "on": "nightly", "to": "done" }
          ],
          "actions": [
            {
              "id": "a1",
              "on": { "transition": "t1" },
              "kind": "InvokeService",
              "capabilityRef": "notify.dispatch",
              "classification": "AP"
            }
          ],
          "guards": []
        }
        """;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static WorkflowDefinitionLoadValidator ValidatorWith(ActionClassification dispatchClass)
    {
        var registry = CapabilityAuthorityRegistry.FromMap(
            new Dictionary<string, ActionClassification>(StringComparer.Ordinal) { [Dispatch] = dispatchClass });
        return new WorkflowDefinitionLoadValidator(new WorkflowAdmissionValidator(registry));
    }

    [Fact]
    public void Admits_a_persisted_definition_that_is_still_admissible()
    {
        // Registry still says notify.dispatch is AP — the authored AP action on the autonomous edge is fine.
        var validator = ValidatorWith(ActionClassification.AP);
        var model = validator.ReadAdmissibleOrThrow(AuthoredAutonomousApAction(), "t1", "nightly-dispatch", "1.0.0");
        Assert.Equal("nightly-dispatch", model.Key);
        Assert.Equal("1.0.0", model.Version);
        Assert.True(validator.Revalidate(AuthoredAutonomousApAction(), "t1", "nightly-dispatch", "1.0.0").IsValid);
    }

    [Fact]
    public void Rejects_a_persisted_definition_when_a_capability_is_reclassified_to_CP()
    {
        // The registry now classifies notify.dispatch as CP. The persisted action, authored AP, no longer
        // matches its registry-derived class — a classification mismatch (a CP capability can no longer be
        // laundered to AP). Load-time re-validation MUST refuse it (fail-closed) — it never executes.
        var validator = ValidatorWith(ActionClassification.CP);

        var ex = Assert.Throws<WorkflowAdmissionException>(
            () => validator.ReadAdmissibleOrThrow(AuthoredAutonomousApAction(), "t1", "nightly-dispatch", "1.0.0"));

        Assert.Contains(ex.Result.Violations, v => v.Code == WorkflowAdmissionCodes.ClassificationMismatch);

        var result = validator.Revalidate(AuthoredAutonomousApAction(), "t1", "nightly-dispatch", "1.0.0");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Reparse_stamps_the_server_owned_identity_not_the_body()
    {
        // The tenant/key/version are server-owned — the mapper stamps them, they are not read from the body.
        var validator = ValidatorWith(ActionClassification.AP);
        var model = validator.ReadAdmissibleOrThrow(AuthoredAutonomousApAction(), "tenant-X", "key-Y", "9.9.9");
        Assert.Equal("tenant-X", model.Tenant);
        Assert.Equal("key-Y", model.Key);
        Assert.Equal("9.9.9", model.Version);
    }
}
