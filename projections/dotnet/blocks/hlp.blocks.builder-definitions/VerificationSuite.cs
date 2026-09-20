using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A role the fixture's actor holds, and the scope it is held in.</summary>
public sealed record VerificationGrant(string RoleKey, string Scope);

/// <summary>A registered external port and the deterministic simulator standing in for it.</summary>
/// <param name="PortId">The port the candidate may reach.</param>
/// <param name="Simulator">The named deterministic simulator; <c>denied</c> blocks the port.</param>
public sealed record VerificationPort(string PortId, string Simulator);

/// <summary>One seed fact the fixture starts from, as a provider-neutral body.</summary>
public sealed record VerificationFact(string RecordType, string BodyJson);

/// <summary>
/// A controlled starting world. Everything that could otherwise make a result coincidental is an
/// explicit input here: the instant, the time zone, the locale, the identifier seed, the collection
/// ordering, the acting persona with the authority it actually holds, and every external port with
/// the deterministic simulator standing in for it. Each case receives a clean clone of this fixture.
/// </summary>
/// <param name="FixtureId">Stable identity, referenced by a case.</param>
/// <param name="Instant">The virtual clock the run starts at.</param>
/// <param name="TimeZone">The IANA zone local values are resolved in.</param>
/// <param name="Locale">The BCP 47 locale formatting and collation resolve in.</param>
/// <param name="IdentifierSeed">The seed every generated identifier derives from.</param>
/// <param name="Ordering">The declared comparison rule for unordered collections.</param>
/// <param name="Actor">The declared persona the action is performed as.</param>
/// <param name="Grants">The authority that persona holds; an empty list is an actor with none.</param>
/// <param name="Ports">Every registered port and its simulator; an unlisted port is denied.</param>
/// <param name="Facts">The seed facts, ordinal by record type and body.</param>
public sealed record VerificationFixture(string FixtureId, DateTimeOffset Instant, string TimeZone,
    string Locale, string IdentifierSeed, string Ordering, string Actor,
    IReadOnlyList<VerificationGrant> Grants, IReadOnlyList<VerificationPort> Ports,
    IReadOnlyList<VerificationFact> Facts);

/// <summary>
/// One expected observation: a registered predicate, the target inside its channel when it needs
/// one, and the expected value as canonical JSON text of the predicate's declared kind.
/// </summary>
/// <param name="PredicateId">The registered predicate.</param>
/// <param name="Target">The target inside the channel, or null when the predicate needs none.</param>
/// <param name="ExpectedJson">The expected value; empty only on a parameterized case, whose rows supply it.</param>
public sealed record VerificationAssertion(string PredicateId, string? Target, string? ExpectedJson)
{
    /// <summary>The assertion's key inside its case: stable, and unique by construction.</summary>
    public string Key => Target is null ? PredicateId : $"{PredicateId}:{Target}";
}

/// <summary>One row of an examples table: the inputs it varies and the value it expects for each assertion.</summary>
/// <param name="RowId">Row identity; it appears in the receipt so a failure names the row.</param>
/// <param name="Values">Input values as canonical JSON text, merged over the case's inputs.</param>
/// <param name="Expected">One expected value per assertion key.</param>
public sealed record VerificationExamplesRow(string RowId, IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> Expected);

/// <summary>
/// One business claim. With no rows it is an invariant: one fixture, one action, one set of
/// assertions. With rows it is a parameterized claim: the same assertions repeated once per row,
/// with the row's identity carried into the result.
/// </summary>
/// <param name="CaseId">Stable identity inside the suite.</param>
/// <param name="Title">The domain-facing claim, for the surfaces; never consulted for pass or fail.</param>
/// <param name="FixtureId">The fixture this case starts from.</param>
/// <param name="ActionId">The registered action under examination.</param>
/// <param name="Inputs">Input values as canonical JSON text, shared by every row.</param>
/// <param name="Rows">The examples table; empty for an invariant.</param>
/// <param name="Assertions">The expected observations; an empty list is refused, never run.</param>
public sealed record VerificationCase(string CaseId, string Title, string FixtureId, string ActionId,
    IReadOnlyDictionary<string, string> Inputs, IReadOnlyList<VerificationExamplesRow> Rows,
    IReadOnlyList<VerificationAssertion> Assertions)
{
    /// <summary>True when the case is an examples table rather than a single invariant.</summary>
    public bool IsParameterized => Rows.Count > 0;
}

/// <summary>A stable refusal code, the public input that caused it, and why.</summary>
public sealed record VerificationRefusal(string Code, string Target, string Message);

/// <summary>
/// A named, versioned collection of business claims. An admitted suite is the only thing a runner
/// will execute, and <see cref="Declare"/> is the only way to make one, so nothing that failed
/// admission can reach a run and be reported green.
/// </summary>
public sealed class VerificationSuite
{
    private const string Contract = "harborline.verification-suite/v1";
    private readonly byte[] canonical;

    private VerificationSuite(string suiteId, string version, IReadOnlyList<VerificationFixture> fixtures,
        IReadOnlyList<VerificationCase> cases, byte[] canonical)
    {
        SuiteId = suiteId;
        Version = version;
        Fixtures = fixtures;
        Cases = cases;
        this.canonical = canonical;
        Digest = Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    /// <summary>Stable suite identity.</summary>
    public string SuiteId { get; }

    /// <summary>The suite version; a changed claim is a new version, not an edited one.</summary>
    public string Version { get; }

    /// <summary>The declared fixtures, ordinal by identity.</summary>
    public IReadOnlyList<VerificationFixture> Fixtures { get; }

    /// <summary>The declared cases, ordinal by identity.</summary>
    public IReadOnlyList<VerificationCase> Cases { get; }

    /// <summary>Lowercase SHA-256 over the canonical suite document.</summary>
    public string Digest { get; }

    /// <summary>The canonical suite document; the digest above is taken over exactly these bytes.</summary>
    public ReadOnlyMemory<byte> Document => canonical;

    /// <summary>Lowercase SHA-256 over one fixture's canonical form, bound individually in the receipt.</summary>
    public string FixtureDigest(string fixtureId)
    {
        var fixture = Fixtures.FirstOrDefault(item => item.FixtureId == fixtureId)
            ?? throw new ArgumentException("verification-fixture-unknown");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteFixture(writer, fixture);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>
    /// Admits a suite, or refuses it by name. Admission is where "a check containing no meaningful
    /// assertion cannot pass" is enforced first: an assertion is meaningful only when it names a
    /// registered predicate, carries an expected value that is well typed for that predicate, and
    /// reads a channel the case's own action actually produces — so it is a comparison that can
    /// fail. A case with no meaningful assertion is refused here and never becomes a suite.
    /// </summary>
    public static VerificationSuite Declare(string suiteId, string version,
        IReadOnlyList<VerificationFixture> fixtures, IReadOnlyList<VerificationCase> cases,
        out IReadOnlyList<VerificationRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(cases);
        var found = new List<VerificationRefusal>();
        void Refuse(string code, string target, string message) => found.Add(new(code, target, message));
        if (string.IsNullOrWhiteSpace(suiteId)) Refuse("verification-suite-id-required", "suiteId", "A suite identity is required.");
        if (string.IsNullOrWhiteSpace(version)) Refuse("verification-suite-version-required", "version", "A suite version is required.");

        var orderedFixtures = fixtures.OrderBy(fixture => fixture.FixtureId, StringComparer.Ordinal).ToArray();
        foreach (var fixture in orderedFixtures) CheckFixture(fixture, Refuse);
        if (orderedFixtures.Select(fixture => fixture.FixtureId).Distinct(StringComparer.Ordinal).Count() != orderedFixtures.Length)
            Refuse("verification-fixture-duplicate", "fixtures", "Two fixtures share one identity.");

        var orderedCases = cases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
        if (orderedCases.Length == 0) Refuse("verification-case-required", "cases", "A suite with no case verifies nothing.");
        if (orderedCases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() != orderedCases.Length)
            Refuse("verification-case-duplicate", "cases", "Two cases share one identity.");
        foreach (var item in orderedCases) CheckCase(item, orderedFixtures, Refuse);

        refusals = Array.AsReadOnly(found.ToArray());
        if (found.Count > 0) throw new VerificationSuiteRefusedException(refusals);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", Contract);
            writer.WriteString("suiteId", suiteId);
            writer.WriteString("version", version);
            writer.WriteString("catalogue", VerificationCatalog.Reference.Digest);
            writer.WriteStartArray("fixtures");
            foreach (var fixture in orderedFixtures) WriteFixture(writer, fixture);
            writer.WriteEndArray();
            writer.WriteStartArray("cases");
            foreach (var item in orderedCases) WriteCase(writer, item);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new(suiteId, version, Array.AsReadOnly(orderedFixtures), Array.AsReadOnly(orderedCases), stream.ToArray());
    }

    /// <summary>
    /// Reads one canonical suite document back into a suite. Parsing goes through the same
    /// <see cref="Declare"/> admission as authoring, so a document cannot carry a case into a run
    /// that authoring would have refused, and a faithful parse reproduces the same digest.
    /// </summary>
    public static VerificationSuite? Parse(string json, out IReadOnlyList<VerificationRefusal> refusals)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            refusals = Array.AsReadOnly(new VerificationRefusal[]
                { new("verification-suite-document-invalid", "suite", "The suite document is not valid JSON.") });
            return null;
        }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("contract", out var contract)
            || contract.GetString() != Contract)
        {
            refusals = Array.AsReadOnly(new VerificationRefusal[]
                { new("verification-suite-contract-unknown", "contract", $"The document does not declare {Contract}.") });
            return null;
        }
        // The document records the catalogue its predicates and actions were resolved against. A
        // later phase extends the catalogue, so re-admitting an older document under a newer one
        // would silently change what its assertions mean; refuse instead of re-deriving a digest.
        if (!root.TryGetProperty("catalogue", out var catalogue) || catalogue.ValueKind != JsonValueKind.String
            || catalogue.GetString() != VerificationCatalog.Reference.Digest)
        {
            refusals = Array.AsReadOnly(new VerificationRefusal[]
            {
                new("verification-catalogue-mismatch", "catalogue",
                    "The suite document was written against another action and predicate catalogue."),
            });
            return null;
        }
        string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
        // Only string members are read. A number, boolean, object or array member is dropped rather
        // than thrown on, so a malformed document reaches a named refusal (an unbound input or an
        // absent expected value) instead of escaping this API as an InvalidOperationException.
        IReadOnlyDictionary<string, string> Map(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
                ? value.EnumerateObject().Where(property => property.Value.ValueKind is JsonValueKind.String)
                    .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        JsonElement.ArrayEnumerator Items(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray() : default;

        var fixtures = Items(root, "fixtures").Select(fixture => new VerificationFixture(
            Text(fixture, "fixtureId"),
            DateTimeOffset.TryParse(Text(fixture, "instant"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var instant) ? instant : default,
            Text(fixture, "timeZone"), Text(fixture, "locale"), Text(fixture, "identifierSeed"),
            Text(fixture, "ordering"), Text(fixture, "actor"),
            Items(fixture, "grants").Select(grant => new VerificationGrant(Text(grant, "roleKey"), Text(grant, "scope"))).ToArray(),
            Items(fixture, "ports").Select(port => new VerificationPort(Text(port, "portId"), Text(port, "simulator"))).ToArray(),
            Items(fixture, "facts").Select(fact => new VerificationFact(Text(fact, "recordType"), Text(fact, "body"))).ToArray()))
            .ToArray();
        var cases = Items(root, "cases").Select(item => new VerificationCase(
            Text(item, "caseId"), Text(item, "title"), Text(item, "fixtureId"), Text(item, "actionId"),
            Map(item, "inputs"),
            Items(item, "rows").Select(row => new VerificationExamplesRow(Text(row, "rowId"),
                Map(row, "values"), Map(row, "expected"))).ToArray(),
            Items(item, "assertions").Select(assertion => new VerificationAssertion(
                Text(assertion, "predicateId"),
                assertion.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String ? target.GetString() : null,
                assertion.TryGetProperty("expected", out var expected) && expected.ValueKind == JsonValueKind.String ? expected.GetString() : null))
                .ToArray()))
            .ToArray();
        return TryDeclare(Text(root, "suiteId"), Text(root, "version"), fixtures, cases, out refusals);
    }

    /// <summary>Admits a suite, returning null and the refusals instead of throwing.</summary>
    public static VerificationSuite? TryDeclare(string suiteId, string version,
        IReadOnlyList<VerificationFixture> fixtures, IReadOnlyList<VerificationCase> cases,
        out IReadOnlyList<VerificationRefusal> refusals)
    {
        try { return Declare(suiteId, version, fixtures, cases, out refusals); }
        catch (VerificationSuiteRefusedException exception)
        {
            refusals = exception.Refusals;
            return null;
        }
    }

    private static void CheckFixture(VerificationFixture fixture, Action<string, string, string> refuse)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var target = string.IsNullOrWhiteSpace(fixture.FixtureId) ? "fixtures" : fixture.FixtureId;
        foreach (var (name, value) in new[]
                 {
                     ("fixtureId", fixture.FixtureId), ("timeZone", fixture.TimeZone), ("locale", fixture.Locale),
                     ("identifierSeed", fixture.IdentifierSeed), ("ordering", fixture.Ordering), ("actor", fixture.Actor),
                 })
            if (string.IsNullOrWhiteSpace(value))
                refuse("verification-fixture-input-required", target, $"The fixture does not declare {name}; it is an explicit input, never a default.");
        if (fixture.Instant == default)
            refuse("verification-fixture-input-required", target,
                "The fixture does not declare instant; it is an explicit input, never a default. A document whose instant does not parse arrives here.");
        foreach (var fact in fixture.Facts ?? [])
        {
            if (string.IsNullOrWhiteSpace(fact.RecordType))
                refuse("verification-fixture-input-required", target, "A seed fact does not name its record type.");
            try { using var _ = JsonDocument.Parse(fact.BodyJson ?? string.Empty); }
            catch (JsonException) { refuse("verification-fixture-fact-invalid", target, "A seed fact body is not valid JSON."); }
        }
        foreach (var port in fixture.Ports ?? [])
            if (string.IsNullOrWhiteSpace(port.PortId) || string.IsNullOrWhiteSpace(port.Simulator))
                refuse("verification-fixture-port-invalid", target, "A declared port must name a port and its simulator.");
    }

    private static void CheckCase(VerificationCase item, IReadOnlyList<VerificationFixture> fixtures,
        Action<string, string, string> refuse)
    {
        ArgumentNullException.ThrowIfNull(item);
        var target = string.IsNullOrWhiteSpace(item.CaseId) ? "cases" : item.CaseId;
        if (string.IsNullOrWhiteSpace(item.CaseId)) refuse("verification-case-id-required", "cases", "A case identity is required.");
        if (string.IsNullOrWhiteSpace(item.Title)) refuse("verification-case-title-required", target, "A case states its claim in domain language.");
        if (!fixtures.Any(fixture => fixture.FixtureId == item.FixtureId))
            refuse("verification-fixture-unknown", target, "The case names a fixture the suite does not declare.");
        var action = VerificationCatalog.Action(item.ActionId);
        if (action is null)
        {
            refuse("verification-action-unknown", target, "The case names an action outside the registered catalogue.");
            return;
        }

        var inputs = item.Inputs ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = item.Rows ?? [];
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.RowId)) refuse("verification-row-id-required", target, "An examples row needs an identity so a failure can name it.");
            var bound = action.Parameters.Where(parameter =>
                !(row.Values ?? new Dictionary<string, string>(StringComparer.Ordinal)).ContainsKey(parameter) && !inputs.ContainsKey(parameter)).ToArray();
            foreach (var parameter in bound)
                refuse("verification-input-unbound", target, $"Row {row.RowId} leaves {parameter} unbound, so the case has no determinate result to assert.");
        }
        if (rows.Count > 1)
        {
            var shape = rows[0].Values?.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray() ?? [];
            foreach (var row in rows.Skip(1))
                if (!shape.SequenceEqual((row.Values?.Keys ?? []).OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal))
                    refuse("verification-row-shape-mismatch", target, $"Row {row.RowId} varies different inputs from the first row, so the rows are not one claim.");
        }
        if (rows.Count == 0)
            foreach (var parameter in action.Parameters.Where(parameter => !inputs.ContainsKey(parameter)))
                refuse("verification-input-unbound", target, $"The case leaves {parameter} unbound, so it has no determinate result to assert.");
        if (rows.Count > 0 && rows.Select(row => row.RowId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            refuse("verification-row-duplicate", target, "Two examples rows share one identity.");

        var assertions = item.Assertions ?? [];
        // The line this whole slice exists for: a case with nothing that can fail is refused here.
        if (assertions.Count == 0)
        {
            refuse("verification-assertion-required", target,
                "The case asserts nothing, so running it could only report a colour. A check containing no meaningful assertion cannot pass.");
            return;
        }
        if (assertions.Select(assertion => assertion.Key).Distinct(StringComparer.Ordinal).Count() != assertions.Count)
            refuse("verification-assertion-duplicate", target, "Two assertions compare the same predicate and target.");
        foreach (var assertion in assertions)
        {
            var predicate = VerificationCatalog.Predicate(assertion.PredicateId);
            if (predicate is null)
            {
                refuse("verification-predicate-unknown", target,
                    $"{assertion.PredicateId} is not a registered predicate, so nothing could evaluate it.");
                continue;
            }
            if (predicate.RequiresTarget == string.IsNullOrWhiteSpace(assertion.Target))
                refuse("verification-assertion-target-invalid", target,
                    $"{assertion.PredicateId} {(predicate.RequiresTarget ? "requires" : "takes no")} target.");
            if (!action.Channels.Contains(predicate.Channel, StringComparer.Ordinal))
                refuse("verification-observation-unavailable", target,
                    $"{assertion.PredicateId} reads the {predicate.Channel} channel, which {item.ActionId} does not produce, so it could never disagree with anything.");
            if (rows.Count == 0)
            {
                if (!VerificationCatalog.IsWellTyped(predicate.Expects, assertion.ExpectedJson))
                    refuse("verification-expected-invalid", target,
                        $"{assertion.PredicateId} has no expected {predicate.Expects} value, so the comparison is vacuous.");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(assertion.ExpectedJson))
                refuse("verification-expected-ambiguous", target,
                    $"{assertion.PredicateId} carries an inline expected value on a parameterized case; the rows state the expectation.");
            foreach (var row in rows)
            {
                (row.Expected ?? new Dictionary<string, string>(StringComparer.Ordinal))
                    .TryGetValue(assertion.Key, out var expected);
                if (!VerificationCatalog.IsWellTyped(predicate.Expects, expected))
                    refuse("verification-expected-invalid", target,
                        $"Row {row.RowId} has no expected {predicate.Expects} value for {assertion.Key}, so that row asserts nothing.");
            }
        }
    }

    private static void WriteFixture(Utf8JsonWriter writer, VerificationFixture fixture)
    {
        writer.WriteStartObject();
        writer.WriteString("fixtureId", fixture.FixtureId);
        writer.WriteString("instant", fixture.Instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("timeZone", fixture.TimeZone);
        writer.WriteString("locale", fixture.Locale);
        writer.WriteString("identifierSeed", fixture.IdentifierSeed);
        writer.WriteString("ordering", fixture.Ordering);
        writer.WriteString("actor", fixture.Actor);
        writer.WriteStartArray("grants");
        foreach (var grant in (fixture.Grants ?? []).OrderBy(grant => grant.RoleKey, StringComparer.Ordinal)
                     .ThenBy(grant => grant.Scope, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("roleKey", grant.RoleKey);
            writer.WriteString("scope", grant.Scope);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("ports");
        foreach (var port in (fixture.Ports ?? []).OrderBy(port => port.PortId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("portId", port.PortId);
            writer.WriteString("simulator", port.Simulator);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("facts");
        foreach (var fact in (fixture.Facts ?? []).OrderBy(fact => fact.RecordType, StringComparer.Ordinal)
                     .ThenBy(fact => fact.BodyJson, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("recordType", fact.RecordType);
            writer.WriteString("body", fact.BodyJson);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCase(Utf8JsonWriter writer, VerificationCase item)
    {
        writer.WriteStartObject();
        writer.WriteString("caseId", item.CaseId);
        writer.WriteString("title", item.Title);
        writer.WriteString("fixtureId", item.FixtureId);
        writer.WriteString("actionId", item.ActionId);
        WriteMap(writer, "inputs", item.Inputs);
        writer.WriteStartArray("assertions");
        foreach (var assertion in (item.Assertions ?? []).OrderBy(assertion => assertion.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("predicateId", assertion.PredicateId);
            if (assertion.Target is null) writer.WriteNull("target"); else writer.WriteString("target", assertion.Target);
            if (string.IsNullOrWhiteSpace(assertion.ExpectedJson)) writer.WriteNull("expected");
            else writer.WriteString("expected", assertion.ExpectedJson);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("rows");
        foreach (var row in (item.Rows ?? []).OrderBy(row => row.RowId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("rowId", row.RowId);
            WriteMap(writer, "values", row.Values);
            WriteMap(writer, "expected", row.Expected);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMap(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, string>? map)
    {
        writer.WriteStartObject(name);
        foreach (var pair in (map ?? new Dictionary<string, string>(StringComparer.Ordinal))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}

/// <summary>Thrown when a suite is not admitted; it carries every refusal, not only the first.</summary>
public sealed class VerificationSuiteRefusedException : Exception
{
    /// <summary>Creates the exception from the refusals that denied admission.</summary>
    public VerificationSuiteRefusedException(IReadOnlyList<VerificationRefusal> refusals)
        : base(string.Join("; ", refusals.Select(refusal => $"{refusal.Code} ({refusal.Target})"))) =>
        Refusals = refusals;

    /// <summary>Every refusal that denied admission.</summary>
    public IReadOnlyList<VerificationRefusal> Refusals { get; }
}
