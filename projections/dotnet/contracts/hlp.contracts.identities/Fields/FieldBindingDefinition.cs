namespace Harborline.Contracts.Fields;

/// <summary>A field in an admitted record/schema model, retaining its kind revision and constraint floor.</summary>
/// <param name="Kind">The exact kind and parameters already owned by the model.</param>
/// <param name="Constraints">The admitted floor that consumers may only narrow.</param>
public sealed record FieldBindingDefinition(FieldKindReference Kind, FieldConstraintDefinition Constraints);
