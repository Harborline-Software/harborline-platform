using System.Text;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>One Booking definition as provider-neutral pack content.</summary>
/// <param name="ContentKind">The transport content kind, <see cref="BookingPackIdentity.ResourceContentKind"/> or <see cref="BookingPackIdentity.BookableContentKind"/>.</param>
/// <param name="DefinitionId">The stable definition identity.</param>
/// <param name="Version">The immutable published version.</param>
/// <param name="Content">The definition with its full envelope, identity, version and tenant included.</param>
public sealed record BookingPackEntry(int ContentKind, string DefinitionId, string Version, PlatformPackageContent Content)
{
    /// <summary>Booking's primitive bucket.</summary>
    public int Primitive => BookingPackIdentity.Primitive;
}

/// <summary>
/// Booking's pack path (DES-0025 section 7). Resources and Bookables travel; an Allocation or a hold
/// never does (L530 to L532). Publication and lifecycle belong to the shared store.
/// </summary>
public static class BookingDefinitionPackage
{
    /// <summary>
    /// Projects one published version into pack content without publishing anything. The store's
    /// identity, version and tenant join the stored envelope, so the entry carries all eight members.
    /// </summary>
    /// <exception cref="DefinitionRefusalException">The revision is a draft, not a Booking kind, or runtime data.</exception>
    public static BookingPackEntry Export(DefinitionRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var document = revision.Document;
        if (document.Key.Kind is not (DefinitionKind.Resources or DefinitionKind.Bookables))
            throw Refuse(BookingDefinitionCodes.PackContentUnsupported, "/registry");
        if (BookingDefinitionAdmission.Parse(document.BodyJson) is not { } body || body["envelope"] is not JsonObject envelope)
            throw Refuse(BookingDefinitionCodes.BodyInvalid, "");
        if (BookingDefinitionAdmission.IsRuntimeData(body)) throw Refuse(BookingDefinitionCodes.RuntimeDataInPack, "/kind");
        if (revision.Status != DefinitionStatus.Published)
            throw Refuse(BookingDefinitionCodes.PublishedVersionRequired, "/versionId");
        envelope["identity"] = document.Key.DefinitionId;
        envelope["version"] = document.Version;
        envelope["tenant"] = document.Key.Tenant;
        return new(BookingPackIdentity.ContentKindOf(document.Key.Kind), document.Key.DefinitionId, document.Version,
            PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(body.ToJsonString())));
    }

    /// <summary>
    /// Install-time structural admission of a pack's Booking entries. Reports every refusal, each
    /// under <c>/entries/{index}</c>, and changes nothing. A Resource in the same pack resolves a
    /// Bookable's requirement alongside the installed ones.
    /// </summary>
    public static IReadOnlyList<DefinitionRefusal> Admit(IReadOnlyList<BookingPackEntry> entries, BookingAdmissionContext installed)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(installed);
        var packResources = entries.Where(entry => entry.ContentKind == BookingPackIdentity.ResourceContentKind)
            .Select(entry => entry.DefinitionId).ToHashSet(StringComparer.Ordinal);
        var context = installed with { IsAdmittedResource = id => packResources.Contains(id) || installed.IsAdmittedResource(id) };
        var refusals = new List<DefinitionRefusal>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var at = $"/entries/{index}";
            var body = BookingDefinitionAdmission.Parse(Encoding.UTF8.GetString(entry.Content.Payload.Span));
            if (body is not null && BookingDefinitionAdmission.IsRuntimeData(body))
            {
                refusals.Add(new(BookingDefinitionCodes.RuntimeDataInPack, at));
                continue;
            }
            DefinitionKind? kind = entry.ContentKind switch
            {
                BookingPackIdentity.ResourceContentKind => DefinitionKind.Resources,
                BookingPackIdentity.BookableContentKind => DefinitionKind.Bookables,
                _ => null,
            };
            if (kind is null)
            {
                refusals.Add(new(BookingDefinitionCodes.PackContentUnsupported, at));
                continue;
            }
            if (body is null)
            {
                refusals.Add(new(BookingDefinitionCodes.BodyInvalid, at));
                continue;
            }
            var tenant = "";
            if (body["envelope"] is JsonObject envelope)
            {
                tenant = BookingDefinitionAdmission.Text(envelope["tenant"]) ?? "";
                foreach (var (member, expected) in new[] { ("identity", entry.DefinitionId), ("version", entry.Version), ("tenant", null) })
                {
                    var value = BookingDefinitionAdmission.Text(envelope[member]);
                    if (string.IsNullOrWhiteSpace(value)) refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, $"{at}/envelope/{member}"));
                    else if (expected is not null && value != expected) refusals.Add(new(BookingDefinitionCodes.EnvelopeMismatch, $"{at}/envelope/{member}"));
                    envelope.Remove(member);
                }
            }
            var document = new DefinitionDocument(new(tenant, kind.Value, entry.DefinitionId), entry.Version, entry.Version, body.ToJsonString());
            refusals.AddRange(BookingDefinitionAdmission.Validate(document, context)
                .Select(refusal => refusal with { Pointer = at + refusal.Pointer }));
        }
        return refusals;
    }

    private static DefinitionRefusalException Refuse(string code, string pointer)
        => new(DefinitionAdmissionPhase.Publish, [new(code, pointer)]);
}
