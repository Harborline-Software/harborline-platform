using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

using Xunit;

namespace Harborline.Architecture.Tests;

public sealed class RulesContractOwnershipArchitectureTests
{
    private const string FormsNamespace = "Harborline.Contracts.Forms";
    private static readonly Regex FormsReference = new(
        @"\bHarborline\s*(?:\.|::)\s*@?Contracts\s*\.\s*@?Forms\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex FormsImport = new(
        "(?:from\\s*|import\\s*(?:\\(\\s*)?|require\\s*\\(\\s*)['\"](?:@harborline-software/contracts/forms(?:/[^'\"]*)?|\\.{1,2}/[^'\"]*/forms\\.[cm]?[jt]s)['\"]",
        RegexOptions.CultureInvariant);

    [Fact]
    public void RuleEngine_metadata_has_no_Forms_type_references()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Harborline.Foundation.RuleEngine.dll");
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var forbidden = metadata.TypeReferences
            .Where(handle => IsFormsNamespace(TypeNamespace(metadata, handle)))
            .Select(handle => TypeNamespace(metadata, handle) + "." + metadata.GetString(metadata.GetTypeReference(handle).Name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(forbidden.Length == 0, "RuleEngine references Forms contracts: " + string.Join(", ", forbidden));
    }

    [Fact]
    public void Runtime_source_has_no_Forms_imports_or_qualified_references()
    {
        var root = TierDirectionArchitectureTests.RepositoryRoot();
        var violations = new List<string>();
        foreach (var (relative, extension, forbidden) in new[]
        {
            ("projections/dotnet/foundation/hlp.foundation.rule-runtime", "*.cs", FormsReference),
            ("projections/typescript/foundation/hlp.foundation.rule-runtime/src", "*.ts", FormsImport),
        })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, relative), extension, SearchOption.AllDirectories)
                .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")))
            {
                if (forbidden.IsMatch(StripComments(File.ReadAllText(file))))
                    violations.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.True(violations.Count == 0, "Runtime imports Forms contracts: " + string.Join(", ", violations));
    }

    [Fact]
    public void Neutral_contract_property_and_converter_closure_has_no_Forms_dependency()
    {
        var assembly = Assembly.Load("Harborline.Contracts");
        var pending = new Stack<Type>();
        foreach (var name in new[]
        {
            "Rules.RuleDefinition", "Rules.RuleTier", "Rules.RuleScope", "Rules.RuleActionKind",
            "Text.InternationalizedText", "Presentation.PresentationHint", "Presentation.PresentationHintSeverityValue",
            "Serialization.Optional`1", "Serialization.OptionalJsonConverterFactory",
            "Serialization.OptionalJsonConverter`1", "Serialization.ClosedEnumJsonConverter`1",
        })
        {
            var type = assembly.GetType("Harborline.Contracts." + name);
            Assert.NotNull(type);
            pending.Push(type);
        }

        var visited = new HashSet<Type>();
        while (pending.TryPop(out var type))
        {
            if (!visited.Add(type)) continue;
            Assert.False(IsFormsNamespace(type.Namespace ?? string.Empty), $"Neutral contract reaches {type}");
            if (type.HasElementType) pending.Push(type.GetElementType()!);
            foreach (var argument in type.GetGenericArguments()) pending.Push(argument);
            if (type.Assembly != assembly) continue;

            if (type.BaseType is not null) pending.Push(type.BaseType);
            foreach (var contract in type.GetInterfaces()) pending.Push(contract);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                pending.Push(field.FieldType);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                pending.Push(property.PropertyType);
                AddAttributeTypes(property.GetCustomAttributesData(), pending);
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                pending.Push(method.ReturnType);
                foreach (var parameter in method.GetParameters()) pending.Push(parameter.ParameterType);
            }
            foreach (var constructor in type.GetConstructors())
                foreach (var parameter in constructor.GetParameters()) pending.Push(parameter.ParameterType);
            AddAttributeTypes(type.GetCustomAttributesData(), pending);
        }
    }

    [Theory]
    [InlineData("Harborline.Contracts.Forms", true)]
    [InlineData("Harborline.Contracts.Forms.Nested", true)]
    [InlineData("Harborline.Contracts.FormsExtra", false)]
    [InlineData("Harborline.Contracts.Rules", false)]
    public void Namespace_fence_matches_the_Forms_owner_and_descendants(string value, bool forbidden) =>
        Assert.Equal(forbidden, IsFormsNamespace(value));

    [Fact]
    public void Nested_type_reference_inherits_its_declaring_namespace()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("ownership-canary"), default, default, default);
        var outer = metadata.AddTypeReference(default, metadata.GetOrAddString(FormsNamespace), metadata.GetOrAddString("Outer"));
        var inner = metadata.AddTypeReference(outer, default, metadata.GetOrAddString("Inner"));
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());

        Assert.Equal("Harborline.Contracts.Forms", TypeNamespace(provider.GetMetadataReader(), inner));
    }

    [Theory]
    [InlineData("using Harborline.Contracts.Forms;", true)]
    [InlineData("global using F = global::Harborline.Contracts.Forms;", true)]
    [InlineData("List<Harborline.Contracts.Forms.FormDefinition> values;", true)]
    [InlineData("// Forms used to own Harborline.Contracts.Forms.RuleDefinition\nusing Harborline.Contracts.Rules;", false)]
    [InlineData("/* Harborline.Contracts.Forms */ using Harborline.Contracts.Rules;", false)]
    public void Source_fence_detects_erased_aliases_and_allows_explanatory_comments(string source, bool forbidden) =>
        Assert.Equal(forbidden, FormsReference.IsMatch(StripComments(source)));

    [Theory]
    [InlineData("import type { RuleDefinition } from '@harborline-software/contracts/forms'", true)]
    [InlineData("type X = import('@harborline-software/contracts/forms').RuleDefinition", true)]
    [InlineData("import '@harborline-software/contracts/forms'", true)]
    [InlineData("import type { RuleDefinition } from '../../../contracts/hlp.contracts.forms/src/forms.js'", true)]
    [InlineData("import type { RuleDefinition } from '@harborline-software/contracts/rules'", false)]
    [InlineData("// from '@harborline-software/contracts/forms'\nexport {}", false)]
    public void Typescript_fence_detects_Forms_entry_imports(string source, bool forbidden) =>
        Assert.Equal(forbidden, FormsImport.IsMatch(StripComments(source)));

    private static bool IsFormsNamespace(string value) =>
        value == FormsNamespace || value.StartsWith(FormsNamespace + ".", StringComparison.Ordinal);

    private static string TypeNamespace(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        return reference.ResolutionScope.Kind == HandleKind.TypeReference
            ? TypeNamespace(metadata, (TypeReferenceHandle)reference.ResolutionScope)
            : metadata.GetString(reference.Namespace);
    }

    private static void AddAttributeTypes(IEnumerable<CustomAttributeData> attributes, Stack<Type> pending)
    {
        foreach (var attribute in attributes)
        {
            pending.Push(attribute.AttributeType);
            foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(argument => argument.TypedValue)))
                if (argument.Value is Type type) pending.Push(type);
        }
    }

    private static string StripComments(string source) =>
        Regex.Replace(source, @"//.*?$|/\*.*?\*/", string.Empty, RegexOptions.Multiline | RegexOptions.Singleline);
}
