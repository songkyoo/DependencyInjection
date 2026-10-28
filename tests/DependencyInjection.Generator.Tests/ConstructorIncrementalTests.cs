using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class ConstructorIncrementalTests
{
    private const string TaggedInConstructor =
        """
        [Inject]
        public Service([Tag(Tags.Value)] in string value)
        {
            Value = value;
        }
        """;

    private const string TaggedValueConstructor =
        """
        [Inject]
        public Service([Tag(Tags.Value)] string value)
        {
            Value = value;
        }
        """;

    private const string DefaultConstructor =
        """
        public Service()
        {
            Value = "original";
        }
        """;

    private const string IntConstructor =
        """
        public Service(int value)
        {
            Value = value.ToString();
        }
        """;

    private const string StringConstructor =
        """
        public Service(string value)
        {
            Value = value;
        }
        """;

    private const string ArrayTagConstructor =
        """
        [Inject]
        public Service([Tag(new[] { 1 })] string value) { Value = value; }
        """;

    private const string MissingInjectConstructor =
        """
        public Service([Tag(Tags.Value)] in string value) { Value = value; }
        """;

    private const string RefConstructor =
        """
        [Inject]
        public Service([Tag(Tags.Value)] ref string value) { Value = value; }
        """;

    private const string DynamicConstructor =
        """
        [Inject]
        public Service([Tag(Tags.Value)] dynamic value) { Value = value; }
        """;

    private const string InaccessibleConstructor =
        """
        [Inject]
        private Service([Tag(Tags.Value)] in string value) { Value = value; }
        """;

    private const string NoPublicConstructor =
        """
        private Service() { Value = "unused"; }
        """;

    private const string Consumer =
        """
        using Macaron.DependencyInjection;

        public static class Probe
        {
            public static string[] Run()
            {
                using var container = DependencyContainerFactory.Create(registrar =>
                {
                    registrar.RegisterSingleton(instance: "updated");
                    registrar.RegisterSingleton(instance: "first", tag: "first");
                    registrar.RegisterSingleton(instance: "second", tag: "second");
                    registrar.RegisterSingleton(instance: 42);
                    registrar.RegisterTransient<Service>();
                    registrar.RegisterTransient<object, Service>(tag: "object");
                });

                return new[]
                {
                    container.Resolve<Service>().Value,
                    ((Service)container.Resolve<object>("object")).Value,
                    container.For<Service>().Create().Value,
                };
            }
        }
        """;

    [Test]
    public void UnrelatedBodyChange_ShouldReuseFactoryAndCreationRendering()
    {
        var unrelated = CSharpSyntaxTree.ParseText(
            text: "public class Unrelated { public int Value() => 1; }",
            options: InputParseOptions,
            path:"Other.cs"
        );
        var input = CreateInput(TaggedInConstructor).AddSyntaxTrees(unrelated);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out _, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);

        var updated = CSharpSyntaxTree.ParseText(
            text: "public class Unrelated { public int Value() => 2; }",
            options: InputParseOptions,
            path: "Other.cs"
        );

        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(unrelated, updated), out var output, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            driver.GetRunResult().Results.Single().TrackedSteps["FactoryGeneration"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified)
        );
        AssertRenderingReason(driver, IncrementalStepRunReason.Cached);
        Assert.That(Execute<string[]>(output), Is.EqualTo(["first", "first", "first"]));
    }

    [Test]
    public void DiagnosticLocationChange_ShouldUpdateUsesAndDeclarationsWithoutRerendering()
    {
        const string invalidSource =
            """
            using Macaron.DependencyInjection;

            public class Invalid { private Invalid() { } }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Invalid>();
                    resolver.For<Invalid>();
                }
            }
            """;
        var invalid = CSharpSyntaxTree.ParseText(invalidSource, InputParseOptions, "Invalid.cs");
        var input = CreateInput(TaggedInConstructor).AddSyntaxTrees(invalid);
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out _, out var firstDiagnostics);

        Assert.That(firstDiagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0006", "MADI0006" }));

        var moved = CSharpSyntaxTree.ParseText("\n\n" + invalidSource, InputParseOptions, "Invalid.cs");

        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(invalid, moved), out var output, out var diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0006", "MADI0006" }));

        for (var i = 0; i < diagnostics.Length; i++)
        {
            Assert.That(diagnostics[i].Location.SourceTree, Is.SameAs(moved));
            Assert.That(
                diagnostics[i].Location.SourceSpan.Start,
                Is.EqualTo(firstDiagnostics[i].Location.SourceSpan.Start + 2)
            );
            Assert.That(
                diagnostics[i].AdditionalLocations.Single().SourceSpan.Start,
                Is.EqualTo(firstDiagnostics[i].AdditionalLocations.Single().SourceSpan.Start + 2)
            );
        }
        AssertRenderingReason(driver, IncrementalStepRunReason.Cached);
        Assert.That(Execute<string[]>(output), Is.EqualTo(["first", "first", "first"]));
    }

    [TestCase(TaggedInConstructor)]
    [TestCase(TaggedValueConstructor)]
    public void TagConstantChange_ShouldRefreshResolvedTagWithoutChangingUses(string constructor)
    {
        var input = CreateInput(constructor);
        var original = input.SyntaxTrees.Single(tree => tree.FilePath == "Tags.cs");
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(first), Is.EqualTo(new[] { "first", "first", "first" }));

        var updated = CSharpSyntaxTree.ParseText(
            text:
                """
                public static class Tags { public const string Value = "second"; }
                """,
            options: InputParseOptions,
            path: "Tags.cs"
        );

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        AssertRenderingReason(driver, IncrementalStepRunReason.Modified);
        Assert.That(Execute<string[]>(output), Is.EqualTo(["second", "second", "second"]));
    }

    [TestCase(DefaultConstructor, "original")]
    [TestCase(IntConstructor, "42")]
    [TestCase(TaggedInConstructor, "first")]
    public void ConstructorChange_ShouldRefreshConstructionWithoutChangingUses(string originalConstructor, string originalValue)
    {
        var input = CreateInput(originalConstructor);
        var original = input.SyntaxTrees.Single(tree => tree.FilePath == "Service.cs");
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(first), Is.EqualTo(Enumerable.Repeat(originalValue, 3)));

        var updated = CSharpSyntaxTree.ParseText(DeclareService(StringConstructor), InputParseOptions, "Service.cs");

        driver = driver.RunGeneratorsAndAnalyze(
            input.ReplaceSyntaxTree(original, updated),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        AssertRenderingReason(driver, IncrementalStepRunReason.Modified);
        Assert.That(Execute<string[]>(output), Is.EqualTo(["updated", "updated", "updated"]));
    }

    [TestCase(ArrayTagConstructor, "MADI0011", 1)]
    [TestCase(MissingInjectConstructor, "MADI0012", 1)]
    [TestCase(RefConstructor, "MADI0009", 1)]
    [TestCase(DynamicConstructor, "MADI0002", 1)]
    [TestCase(InaccessibleConstructor, "MADI0008", 3)]
    [TestCase(NoPublicConstructor, "MADI0006", 3)]
    public void InvalidDeclaration_ShouldRemoveOutputsAndRestoreThemAfterCorrection(string constructor, string expectedId, int diagnosticCount)
    {
        var input = CreateInput(TaggedInConstructor);
        var original = input.SyntaxTrees.Single(tree => tree.FilePath == "Service.cs");
        var driver = CreateDriver(trackIncrementalSteps: true)
            .RunGeneratorsAndAnalyze(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(first), Is.EqualTo(new[] { "first", "first", "first" }));

        var invalid = CSharpSyntaxTree.ParseText(DeclareService(constructor), InputParseOptions, "Service.cs");

        driver = driver.RunGeneratorsAndAnalyze(input.ReplaceSyntaxTree(original, invalid), out _, out diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(Enumerable.Repeat(expectedId, diagnosticCount)));

        var sources = driver.GetRunResult().Results.Single().GeneratedSources;

        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.Zero);
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Not.Contain("typeof(global::Service)")
        );

        driver.RunGeneratorsAndAnalyze(input, out var restored, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string[]>(restored), Is.EqualTo(["first", "first", "first"]));
    }

    private static CSharpCompilation CreateInput(string constructor)
    {
        var service = CSharpSyntaxTree.ParseText(DeclareService(constructor), InputParseOptions, "Service.cs");
        var tags = CSharpSyntaxTree.ParseText(
            text:
                """
                public static class Tags { public const string Value = "first"; }
                """,
            options: InputParseOptions,
            path: "Tags.cs"
        );

        return CreateCompilation(Consumer).AddSyntaxTrees(service, tags);
    }

    private static string DeclareService(string constructor)
    {
        return
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string Value { get; }

                {{constructor}}
            }
            """;
    }

    private static void AssertRenderingReason(GeneratorDriver driver, IncrementalStepRunReason expected)
    {
        var steps = driver.GetRunResult().Results.Single().TrackedSteps;

        Assert.That(steps["FactoriesSource"].Single().Outputs.Single().Reason, Is.EqualTo(expected));
        Assert.That(steps["CreationSource"].Single().Outputs.Single().Reason, Is.EqualTo(expected));
    }
}
