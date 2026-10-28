using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class InjectionParameterDiagnosticTests
{
    [TestCase("ref int", "MADI0009")]
    [TestCase("out int", "MADI0009")]
    [TestCase("System.Span<int>", "MADI0009")]
    [TestCase("System.ReadOnlySpan<int>", "MADI0009")]
    [TestCase("int*", "MADI0009")]
    [TestCase("delegate*<void>", "MADI0009")]
    [TestCase("dynamic", "MADI0002")]
    [TestCase("dynamic[]", "MADI0002")]
    [TestCase("System.Collections.Generic.List<dynamic>", "MADI0002")]
    public void UnusedTaggedParameter_ShouldReportUnsupportedShapeAtParameter(string parameterType, string expectedId)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public unsafe class Service
            {
                [Inject]
                public Service([Tag("tagged")] {{parameterType}} value) { value = default; }
            }
            """;
        var input = CreateCompilation(source);

        input = input.WithOptions(input.Options.WithAllowUnsafe(true));

        CreateDriver().RunGeneratorsAndAnalyze(input, out var output, out var diagnostics);

        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo(expectedId));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo("value"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Tag").And.Contain("Service").And.Contain("value"));
        AssertCompiles(output);
    }

    [TestCase("ref int", "value = 0;", "MADI0009")]
    [TestCase("out int", "value = 0;", "MADI0009")]
    [TestCase("System.Span<int>", "", "MADI0009")]
    [TestCase("dynamic", "", "MADI0002")]
    public void RepeatedUses_ShouldReportShapeOnceAndPreserveValidFactory(string parameterType, string body, string expectedId)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject]
                public Service([Tag("tagged")] {{parameterType}} value) { {{body}} }
            }

            public class Valid { }

            public static class Configuration
            {
                public static void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Service>();
                    registrar.RegisterSingleton<object, Service>();
                    resolver.For<Service>();
                    resolver.For<Service>();
                    registrar.RegisterTransient<Valid>();
                    resolver.For<Valid>().Create();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]));

        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("typeof(global::Service)").And.Contain("typeof(global::Valid)"));
        AssertCompiles(compilation);
    }

    [TestCase("ref int", "value = 0;", "MADI0009")]
    [TestCase("out int", "value = 0;", "MADI0009")]
    [TestCase("System.Span<int>", "", "MADI0009")]
    [TestCase("dynamic", "", "MADI0002")]
    public void ReferencedTaggedParameter_ShouldReportAtUse(string parameterType, string body, string expectedId)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }
                [Inject]
                private Service([Tag("tagged")] {{parameterType}} value) { {{body}} }
            }
            """, assemblyName: "ExternalServices"
        ));
        const string source =
            """
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Configure(IDependencyRegistrar registrar) => registrar.RegisterTransient<Service>();
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            source,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo(expectedId));
        Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length), Is.EqualTo("Service"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Tag").And.Contain("value"));
        AssertCompiles(compilation);
    }

    [Test]
    public void GenericAndInParameters_ShouldRemainValidWithoutRegistration()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                [Inject]
                public Service([Tag] T first, [Tag] in T second, [Tag] T[] third) { }
            }
            """
        );

        AssertCompiles(compilation);
    }

    [Test]
    public void ArrayMissingInjectAndRef_ShouldReportIndependentErrors()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service([Tag(new[] { 1 })] ref int value) { }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EquivalentTo(["MADI0011", "MADI0012", "MADI0009"]));
        AssertCompiles(compilation);
    }

    [TestCase("[Tag(Missing)] ref int")]
    [TestCase("[Tag, Tag] ref int")]
    [TestCase("[Tag] Missing")]
    public void CompilerRejectedParameter_ShouldNotAddGeneratorDiagnostics(string parameter)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject]
                public Service({{parameter}} value) { }
            }
            """
        );

        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
        Assert.That(diagnostics, Is.Empty);
    }
}
