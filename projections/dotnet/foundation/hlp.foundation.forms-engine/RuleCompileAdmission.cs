using Contract = Harborline.Contracts.Forms;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.RuleEngine.Compilation;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>
/// Rejects definitions whose Tier-2 rules or page guards cannot compile on the
/// same .NET rule runtime used by render and submit.
/// </summary>
public static class RuleCompileAdmission
{
    /// <summary>
    /// Compiles the complete Tier-2 rule set and every page visibility guard.
    /// Authoring hosts must invoke this before persisting a definition revision.
    /// </summary>
    public static void ValidateOrThrow(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Overlay.Rules.Count > 0)
        {
            try
            {
                _ = RuleCompiler.Compile(definition.Overlay.Rules.Select(FormContractMapper.ToContractRule).ToArray());
            }
            catch (RuleCompilationException exception)
            {
                var subject = string.IsNullOrWhiteSpace(exception.RuleId)
                    ? "the rule set"
                    : $"rule '{exception.RuleId}'";
                throw new FormDefinitionValidationException(
                    definition.Id,
                    $"{subject} does not compile ({exception.Code}): {exception.Message}",
                    FormDefinitionCodes.RulesUncompilable);
            }
        }

        foreach (var page in definition.Overlay.Pages ?? Array.Empty<FormPage>())
        {
            if (string.IsNullOrWhiteSpace(page.VisibleWhen))
            {
                continue;
            }

            var guard = new Contract.RuleDefinition
            {
                Id = $"page-guard:{page.Id}",
                Tier = Contract.RuleTier.JsonLogic,
                Scope = Contract.RuleScope.Schema,
                ScopeTarget = string.Empty,
                Expression = page.VisibleWhen,
                Action = Contract.RuleActionKind.Validate,
            };

            try
            {
                _ = RuleCompiler.Compile([guard]);
            }
            catch (RuleCompilationException exception)
            {
                throw new FormDefinitionValidationException(
                    definition.Id,
                    $"page '{page.Id}' has a VisibleWhen guard that does not compile ({exception.Code}): {exception.Message}",
                    FormDefinitionCodes.RulesGuardUncompilable,
                    page.Id);
            }
        }
    }
}
