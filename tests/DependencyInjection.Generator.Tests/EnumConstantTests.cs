using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class EnumConstantTests
{
    private const string EnumDeclaration =
        """
        namespace @event
        {
            [System.Flags]
            public enum @class : ulong
            {
                Ready = 1,
                @default = 2,
                Both = 3,
                FirstAlias = 8,
                SecondAlias = 8,
            }
        }
        """;

    private const string RenamedEnumDeclaration =
        """
        namespace @event
        {
            [System.Flags]
            public enum @class : ulong
            {
                Started = 1,
                @default = 2,
                Both = 3,
                FirstAlias = 8,
                SecondAlias = 8,
            }
        }
        """;

    private const string ReorderedEnumDeclaration =
        """
        namespace @event
        {
            [System.Flags]
            public enum @class : ulong
            {
                Ready = 1,
                @default = 2,
                Both = 3,
                SecondAlias = 8,
                FirstAlias = 8,
            }
        }
        """;

    private const string Consumer =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static bool Run()
            {
                using var container = DependencyContainerFactory.Create(registrar =>
                {
                    registrar.RegisterSingleton(instance: "tagged", tag: Expected.Value);
                    registrar.RegisterSingleton(instance: "numeric", tag: unchecked((ulong)Expected.Value));
                    registrar.RegisterTransient<TaggedService>();
                });

                return container.Resolve<TaggedService>().Value == "tagged"
                    && container.For<TaggedService>().Create().Value == "tagged"
                    && container.For<AssistedService>().Create().Value == Expected.Value;
            }
        }
        """;

    [TestCase("Mode.Ready", "global::@event.@class.Ready")]
    [TestCase("Mode.@default", "global::@event.@class.@default")]
    [TestCase("Mode.SecondAlias", "global::@event.@class.FirstAlias")]
    [TestCase("Mode.Ready | Mode.@default", "global::@event.@class.Both")]
    [TestCase("(Mode)5", "(global::@event.@class)(5)")]
    [TestCase("(Mode)ulong.MaxValue", "(global::@event.@class)(18446744073709551615)")]
    public void EnumConstants_ShouldUseReadableTypedExpressions(string expression, string expectedExpression)
    {
        var input = CreateCompilation(Consumer).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(EnumDeclaration, InputParseOptions, "Enum.cs"),
            CSharpSyntaxTree.ParseText(ServiceDeclarations(expression), InputParseOptions, "Services.cs")
        );
        var (compilation, sources) = Generate(input);

        AssertExpressions(sources, expectedExpression);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning), Is.Empty);
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("sbyte", "sbyte.MinValue")]
    [TestCase("short", "short.MinValue")]
    [TestCase("int", "int.MinValue")]
    [TestCase("long", "long.MinValue")]
    [TestCase("byte", "byte.MaxValue")]
    [TestCase("ushort", "ushort.MaxValue")]
    [TestCase("uint", "uint.MaxValue")]
    [TestCase("ulong", "ulong.MaxValue")]
    public void EnumUnderlyingType_ShouldPreserveExactConstants(string underlyingType, string value)
    {
        var declaration =
            $$"""
            namespace @event
            {
                public enum @class : {{underlyingType}} { Ready = {{value}} }
            }
            """;
        var input = CreateCompilation(Consumer).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(declaration, InputParseOptions, "Enum.cs"),
            CSharpSyntaxTree.ParseText(ServiceDeclarations("Mode.Ready"), InputParseOptions, "Services.cs")
        );
        var (compilation, sources) = Generate(input);

        AssertExpressions(sources, "global::@event.@class.Ready");
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ExternalEnumConstants_ShouldPreserveNamesAndNullableDefaults()
    {
        var image = EmitAssembly(CreateExternalServices(EnumDeclaration, "Mode.@default"));
        var (compilation, sources) = Generate(Consumer, additionalReferences: MetadataReference.CreateFromImage(image));

        AssertExpressions(sources, "global::@event.@class.@default");
        Assert.That(Execute<bool>(compilation, image), Is.True);
    }

    [Test]
    public void ObsoleteMember_ShouldUseTypedValueWithoutIntroducingDiagnostics()
    {
        const string declaration =
            """
            namespace @event
            {
                public enum @class
                {
                    [System.Obsolete("Removed", true)] Removed = 1,
                }
            }
            """;
        var input = CreateCompilation(Consumer).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(declaration, InputParseOptions, "Enum.cs"),
            CSharpSyntaxTree.ParseText(ServiceDeclarations("(Mode)1"), InputParseOptions, "Services.cs")
        );
        var (compilation, sources) = Generate(input);

        AssertExpressions(sources, "(global::@event.@class)(1)");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning), Is.Empty);
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void EnumMemberRename_ShouldRefreshTagAndDefaultWithoutChangingTheRequest()
    {
        var original = CSharpSyntaxTree.ParseText(EnumDeclaration, InputParseOptions, "Enum.cs");
        var input = CreateCompilation(Consumer).AddSyntaxTrees(
            original,
            CSharpSyntaxTree.ParseText(ServiceDeclarations("(Mode)1"), InputParseOptions, "Services.cs")
        );
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(first), Is.True);

        var updated = CSharpSyntaxTree.ParseText(RenamedEnumDeclaration, InputParseOptions, "Enum.cs");

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        AssertExpressions(driver.GetRunResult().Results.Single().GeneratedSources, "global::@event.@class.Started");
        Assert.That(Execute<bool>(output), Is.True);
    }

    [Test]
    public void AliasReordering_ShouldReuseTagAndDefaultRendering()
    {
        var original = CSharpSyntaxTree.ParseText(EnumDeclaration, InputParseOptions, "Enum.cs");
        var input = CreateCompilation(Consumer).AddSyntaxTrees(
            original,
            CSharpSyntaxTree.ParseText(ServiceDeclarations("(Mode)8"), InputParseOptions, "Services.cs")
        );
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        var updated = CSharpSyntaxTree.ParseText(ReorderedEnumDeclaration, InputParseOptions, "Enum.cs");
        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(original, updated), out var output, out diagnostics);


        Assert.That(diagnostics, Is.Empty);

        var result = driver.GetRunResult().Results.Single();

        Assert.That(
            result.TrackedSteps["FactoriesSource"].SelectMany(step => step.Outputs).Select(output => output.Reason),
            Is.All.EqualTo(IncrementalStepRunReason.Cached)
        );
        Assert.That(
            result.TrackedSteps["CreationSource"].SelectMany(step => step.Outputs).Select(output => output.Reason),
            Is.All.EqualTo(IncrementalStepRunReason.Cached)
        );
        AssertExpressions(result.GeneratedSources, "global::@event.@class.FirstAlias");
        Assert.That(Execute<bool>(output), Is.True);
    }

    [Test]
    public void ExternalEnumMemberRename_ShouldRefreshTagAndDefault()
    {
        var firstImage = EmitAssembly(CreateExternalServices(EnumDeclaration, "(Mode)1"));
        var secondImage = EmitAssembly(CreateExternalServices(RenamedEnumDeclaration, "(Mode)1"));
        var firstReference = MetadataReference.CreateFromImage(firstImage);
        var secondReference = MetadataReference.CreateFromImage(secondImage);
        var input = CreateCompilation(Consumer, additionalReferences: firstReference);
        var driver = CreateDriver().RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(first, firstImage), Is.True);

        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceReference(firstReference, secondReference), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        AssertExpressions(driver.GetRunResult().Results.Single().GeneratedSources, "global::@event.@class.Started");
        Assert.That(Execute<bool>(output, secondImage), Is.True);
    }

    private static string ServiceDeclarations(string expression)
    {
        return
            $$"""
            #nullable enable
            using Macaron.DependencyInjection;
            using Mode = @event.@class;

            public static class Expected
            {
                public const Mode Value = {{expression}};
            }

            public sealed class TaggedService
            {
                public string Value { get; }
                [Inject] public TaggedService([Tag(Expected.Value)] in string value) { Value = value; }
            }

            public sealed class AssistedService
            {
                public Mode? Value { get; }
                [Inject] public AssistedService([Assisted] in Mode? value = Expected.Value) { Value = value; }
            }
            """;
    }

    private static CSharpCompilation CreateExternalServices(string enumDeclaration, string expression)
    {
        return CreateCompilation(enumDeclaration, "ExternalServices").AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(ServiceDeclarations(expression), InputParseOptions, "Services.cs")
        );
    }

    private static void AssertExpressions(IEnumerable<GeneratedSourceResult> sources, string expectedExpression)
    {
        var source = string.Join("\n", sources.Select(source => source.SourceText.ToString()));

        Assert.That(source, Does.Contain($"value = {expectedExpression}"));
        Assert.That(source, Does.Contain($"= {expectedExpression};"));
    }
}
