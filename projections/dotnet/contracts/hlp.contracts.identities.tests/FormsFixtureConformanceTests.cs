using System.Text.Json;

using Harborline.Contracts.Forms;
using Xunit;

namespace Harborline.Contracts.Tests;

public sealed class FormsFixtureConformanceTests
{
    [Fact]
    public void Frozen_forms_fixtures_pass_the_generated_dotnet_projection()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();

        foreach (var fixture in cases)
        {
            var id = fixture.GetProperty("id").GetString();
            var operation = fixture.GetProperty("operation").GetString();
            switch (operation)
            {
                case "surface.exports":
                    Assert.Equal(fixture.GetProperty("expected").GetProperty("declarations").GetInt32(), FormsContractSurface.Exports.Count);
                    break;
                case "closed.values":
                    AssertClosedValues(fixture);
                    break;
                case "constant.value":
                    Assert.Equal(FormsContractConstants.HARBORLINE_JSONLOGIC_V1, fixture.GetProperty("expected").GetString());
                    break;
                case "wire.read":
                case "wire.round-trip":
                    AssertWire(fixture);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown fixture operation {operation} ({id}).");
            }
        }

        Assert.Equal(27, cases.Length);
    }

    private static void AssertClosedValues(JsonElement fixture)
    {
        if (fixture.TryGetProperty("type", out var type))
        {
            Assert.Equal(
                fixture.GetProperty("expected").EnumerateArray().Select(value => value.GetString()),
                FormsContractSurface.ClosedValues[type.GetString()!]);
            return;
        }

        foreach (var expected in fixture.GetProperty("expected").EnumerateObject())
            Assert.Equal(expected.Value.EnumerateArray().Select(value => value.GetString()), FormsContractSurface.ClosedValues[expected.Name]);
    }

    private static void AssertWire(JsonElement fixture)
    {
        var type = fixture.GetProperty("type").GetString()!;
        var input = fixture.GetProperty("input");
        if (fixture.TryGetProperty("expectedError", out var expectedError))
        {
            var error = Assert.Throws<FormsWireException>(() => FormsWire.Deserialize(type, input.GetRawText()));
            Assert.Equal(expectedError.GetString(), error.Code);
            return;
        }

        var value = FormsWire.Deserialize(type, input.GetRawText());
        using var actual = JsonDocument.Parse(FormsWire.Serialize(type, value));
        var expected = fixture.GetProperty("expected");
        var expectedValue = expected.ValueKind == JsonValueKind.String ? input : expected;
        Assert.True(JsonElement.DeepEquals(expectedValue, actual.RootElement),
            $"{fixture.GetProperty("id").GetString()} expected {expectedValue.GetRawText()} but received {actual.RootElement.GetRawText()}");
    }

    private static string FindFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "conformance", "hlp.contracts.forms", "fixtures.yaml");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Unable to locate conformance/hlp.contracts.forms/fixtures.yaml.");
    }
}
