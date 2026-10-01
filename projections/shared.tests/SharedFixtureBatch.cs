global using Harborline.Conformance;

using System.Text.Json;

namespace Harborline.Conformance;

// Each theory row gets its own xUnit test instance. No fixture is injected into process-wide state.
public static class SharedFixtureBatch
{
    private static readonly object ResultLock = new();
    private static readonly AsyncLocal<string?> Fixture = new();
    public static string? Current => Fixture.Value ?? Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");

    public static IEnumerable<object?[]> Cases(string moduleId)
    {
        var path = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_BATCH");
        if (string.IsNullOrEmpty(path))
        {
            var single = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
            using var fixture = string.IsNullOrEmpty(single) ? null : JsonDocument.Parse(single);
            yield return new object?[] { fixture?.RootElement.GetProperty("id").GetString() ?? "(native)", single };
            yield break;
        }
        using var batch = JsonDocument.Parse(File.ReadAllText(path));
        if (batch.RootElement.GetProperty("moduleId").GetString() != moduleId)
            throw new InvalidOperationException("Conformance batch belongs to another module.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cases = batch.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        if (cases.Length == 0) throw new InvalidOperationException("Conformance batch has no cases.");
        foreach (var fixture in cases)
        {
            var id = fixture.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw new InvalidOperationException("Conformance batch has an invalid or duplicate case id.");
            yield return new object?[] { id, fixture.GetRawText() };
        }
    }

    public static void Run(string caseId, string? raw, Action assertion)
    {
        Execute(caseId, raw, () => { assertion(); return Task.CompletedTask; }).GetAwaiter().GetResult();
    }

    public static Task RunAsync(string caseId, string? raw, Func<Task> assertion) => Execute(caseId, raw, assertion);

    private static async Task Execute(string caseId, string? raw, Func<Task> assertion)
    {
        var passed = false;
        string? failure = null;
        var previous = Fixture.Value;
        Fixture.Value = raw;
        var output = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_RESULTS");
        try
        {
            if (!string.IsNullOrEmpty(output) && raw is null)
                throw new InvalidOperationException("A batch result requires a fixture.");
            if (raw is not null)
            {
                using var fixture = JsonDocument.Parse(raw);
                if (fixture.RootElement.GetProperty("id").GetString() != caseId)
                    throw new InvalidOperationException("Theory case identity differs from its fixture.");
            }
            await assertion();
            passed = true;
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            Fixture.Value = previous;
            if (!string.IsNullOrEmpty(output))
            {
                var row = JsonSerializer.Serialize(new { caseId, passed, failureOutput = failure });
                lock (ResultLock) File.AppendAllText(output, row + "\n");
            }
        }
    }
}
