using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
builder.Services.AddHarborlineFormsAuthoringPublisher();
var app = builder.Build();

app.MapPut("/api/forms/{id}", async (
    string id,
    AuthoringRequest request,
    IFormDefinitionAuthoringPublisher publisher,
    CancellationToken cancellationToken) =>
{
    try
    {
        var published = await publisher.RegisterAndPublishAsync(
            BuildDefinition(id, request),
            cancellationToken);
        return Results.Ok(new { id = published.Id.Value, status = published.Status.ToString() });
    }
    catch (FormDefinitionValidationException exception)
    {
        return Results.UnprocessableEntity(new { code = exception.Code, error = exception.Message });
    }
});

app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();
try
{
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses;
    var address = addresses?.SingleOrDefault()
        ?? throw new InvalidOperationException("The package-only authoring host did not bind a loopback address.");
    using var client = new HttpClient { BaseAddress = new Uri(address) };

    var valid = await client.PutAsJsonAsync("/api/forms/inspection", new AuthoringRequest("true"));
    if (valid.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"Package-only authoring host rejected a valid definition: {valid.StatusCode}.");

    var malformed = await client.PutAsJsonAsync("/api/forms/malformed", new AuthoringRequest("{not-json"));
    if (malformed.StatusCode != HttpStatusCode.UnprocessableEntity)
        throw new InvalidOperationException($"Package-only authoring host did not reject malformed rules: {malformed.StatusCode}.");

    var policyRejected = await client.PutAsJsonAsync(
        "/api/forms/policy-rejected",
        new AuthoringRequest("true", "piii"));
    if (policyRejected.StatusCode != HttpStatusCode.UnprocessableEntity)
        throw new InvalidOperationException($"Package-only authoring host did not reject unknown classification: {policyRejected.StatusCode}.");

    var store = app.Services.GetRequiredService<IFormDefinitionStore>();
    try
    {
        await store.GetAsync(
            new TenantId("tenant-package-host"),
            new FormDefinitionId("malformed"),
            SemanticVersion.Parse("1.0.0"));
        throw new InvalidOperationException("Rejected package-only authoring request persisted a definition.");
    }
    catch (FormDefinitionNotFoundException)
    {
    }
    try
    {
        await store.GetAsync(
            new TenantId("tenant-package-host"),
            new FormDefinitionId("policy-rejected"),
            SemanticVersion.Parse("1.0.0"));
        throw new InvalidOperationException("Policy-rejected package-only authoring request persisted a definition.");
    }
    catch (FormDefinitionNotFoundException)
    {
    }

    Console.WriteLine("PACKAGE_HOST_PASS: package-only Forms Engine HTTP authoring host admitted valid rules, rejected malformed rules and unknown classification before atomic persistence, and resolved its transitive Harborline closure");
}
finally
{
    await app.StopAsync();
}

static FormDefinition BuildDefinition(string id, AuthoringRequest request) =>
    new(
        new FormDefinitionId(id),
        SemanticVersion.Parse("1.0.0"),
        FormDefinitionStatus.Draft,
        new TenantId("tenant-package-host"),
        IdentityRef.System,
        new SchemaId("sha256:package-host"),
        new HarborlineOverlay(
            new Dictionary<string, FieldOverlay>
            {
                ["condition"] = new(
                    InternationalizedText.FromInvariant("Condition"),
                    Aspects: request.ClassificationCode is null
                        ? null
                        : new AspectOverlay(new ClassificationAspect(
                            [new Tag("harborline/data-classification", request.ClassificationCode)]))),
            },
            [new FormSection(
                "inspection",
                InternationalizedText.FromInvariant("Inspection"),
                ["condition"],
                new SectionAccess([Harborline.Contracts.Authorization.RoleReference.Domain("inspector")], [Harborline.Contracts.Authorization.RoleReference.Domain("inspector")]))],
            [new RuleDefinition(
                "condition.compute",
                RuleTier.JsonLogic,
                RuleScope.Field,
                "condition",
                request.Expression,
                RuleActionKind.Compute)]),
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

sealed record AuthoringRequest(string Expression, string? ClassificationCode = null);
