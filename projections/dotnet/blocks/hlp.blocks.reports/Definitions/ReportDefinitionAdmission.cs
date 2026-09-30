using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.MeasureCatalogue;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Reports.Definitions;

/// <summary>The measure catalogue a report resolves against, with the name a refusal reports it by.</summary>
/// <param name="Name">The catalogue's name.</param>
/// <param name="Measures">The catalogue.</param>
public sealed record NamedMeasureCatalogue(string Name, IMeasureCatalogue Measures);

/// <summary>Phase-aware admission for report definitions.</summary>
public static class ReportDefinitionAdmission
{
    /// <summary>
    /// Admits a definition. Every measure path is resolved through <see cref="IMeasureCatalogue.ResolveAsync"/> at
    /// every phase, so a measure the catalogue lacks is refused by name at author time and again at install
    /// (reports-auth-15). Resolution reads no row.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="phase">The admission stage.</param>
    /// <param name="window">The host's application-contract window from the platform seed (T-572).</param>
    /// <param name="catalogue">The catalogue the measures resolve against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict; admitted when it carries no refusal.</returns>
    public static async ValueTask<ReportAdmissionReport> AdmitAsync(ReportDefinition definition, DefinitionAdmissionPhase phase,
        DefinitionContractWindow window, NamedMeasureCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(catalogue);
        var refusals = new List<ReportRefusal>();

        var envelope = definition.Envelope;
        if (envelope is null)
        {
            if (phase != DefinitionAdmissionPhase.Author) refusals.Add(new(ReportDefinitionCodes.EnvelopeRequired, "/envelope"));
        }
        else
        {
            if (envelope.Identity != definition.Key || envelope.Version != definition.Version || envelope.Tenant != definition.Tenant)
            {
                refusals.Add(new(ReportDefinitionCodes.EnvelopeMismatch, "/envelope"));
            }
            // T-572 (rulings 85-88): the contract travels with the envelope, inside the host's window.
            var contract = window.Check(envelope.Contract, null);
            if (contract is not null) refusals.Add(new(contract.Code, contract.Pointer));
        }

        for (var index = 0; index < definition.Measures.Count; index++)
        {
            var path = definition.Measures[index].Measure;
            var pointer = $"/measures/{index}/measure";
            if (!MeasureRef.TryParse(path, out var reference))
            {
                refusals.Add(new(ReportDefinitionCodes.MeasureMalformed, pointer, path, catalogue.Name));
            }
            else if (await catalogue.Measures.ResolveAsync(reference!, cancellationToken).ConfigureAwait(false) is null)
            {
                refusals.Add(new(ReportDefinitionCodes.MeasureUnknown, pointer, path, catalogue.Name));
            }
        }

        var parameters = definition.Parameters ?? Array.Empty<ReportParameterDefinition>();
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var pointer = $"/parameters/{index}";
            if (!Enum.IsDefined(parameter.Type))
            {
                refusals.Add(new(ReportDefinitionCodes.ParameterTypeUnsupported, pointer, parameter.Name));
                continue;
            }
            if (parameter.Default is null || parameter.Default.Type != parameter.Type)
            {
                refusals.Add(new(ReportDefinitionCodes.ParameterDefaultTypeMismatch, pointer, parameter.Name));
                continue;
            }
            if (!parameter.Default.IsValid)
            {
                refusals.Add(new(ReportDefinitionCodes.ParameterDefaultInvalid, pointer, parameter.Name));
                continue;
            }

            var isAsAt = string.Equals(parameter.Name, "as_at", StringComparison.Ordinal);
            if (isAsAt && parameter.Type != ReportParameterType.DateTime)
            {
                refusals.Add(new(ReportDefinitionCodes.AsAtMustBeDateTime, pointer, parameter.Name));
            }
            else if (isAsAt && string.IsNullOrWhiteSpace(parameter.EffectiveDatedType))
            {
                refusals.Add(new(ReportDefinitionCodes.AsAtEffectiveTypeRequired, pointer, parameter.Name));
            }
            else if (!isAsAt && parameter.EffectiveDatedType is not null)
            {
                refusals.Add(new(ReportDefinitionCodes.AsAtNameRequired, pointer, parameter.Name));
            }
        }

        return new ReportAdmissionReport(phase, refusals);
    }

    /// <summary>Returns the definition when admitted and throws <see cref="ReportAdmissionException"/> otherwise.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="phase">The admission stage.</param>
    /// <param name="window">The host's application-contract window.</param>
    /// <param name="catalogue">The catalogue the measures resolve against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The admitted definition.</returns>
    public static async ValueTask<ReportDefinition> RequireAsync(ReportDefinition definition, DefinitionAdmissionPhase phase,
        DefinitionContractWindow window, NamedMeasureCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        var report = await AdmitAsync(definition, phase, window, catalogue, cancellationToken).ConfigureAwait(false);
        return report.Refusals.Count == 0 ? definition : throw new ReportAdmissionException(report);
    }
}
