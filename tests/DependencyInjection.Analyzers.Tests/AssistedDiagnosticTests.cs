using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class AssistedDiagnosticTests
{
    [TestCase("public Service([Assisted] int value) { }", "MADI0011", "Assisted")]
    [TestCase("[Inject] public Service([Assisted] ref int value) { }", "MADI0008", "value")]
    [TestCase("[Inject] public Service([Assisted] out int value) { value = 0; }", "MADI0008", "value")]
    [TestCase("[Inject] public Service([Assisted] System.Span<int> value) { }", "MADI0008", "value")]
    [TestCase("[Inject] public Service([Assisted] dynamic value) { }", "MADI0002", "value")]
    [TestCase("[Inject] public Service([Tag, Assisted] int value) { }", "MADI0013", "Assisted")]
    public void UnusedAssistedDeclaration_ShouldReportAtAttributeOrParameter(string constructor, string id, string locationText)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                {{constructor}}
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();
        var span = diagnostic.Location.SourceSpan;
        var actualLocation = source.Substring(span.Start, span.Length);

        Assert.That(diagnostic.Id, Is.EqualTo(id));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(actualLocation, Is.EqualTo(locationText));
        Assert.That(diagnostic.GetMessage(), Does.Contain("Assisted").And.EndsWith("."));
        AssertCompiles(compilation);
    }

    [TestCase("public Service([Assisted] int value) { }", "MADI0011")]
    [TestCase("[Inject] public Service([Assisted] ref int value) { }", "MADI0008")]
    [TestCase("[Inject] public Service([Assisted] dynamic value) { }", "MADI0002")]
    [TestCase("[Inject] public Service([Tag, Assisted] int value) { }", "MADI0013")]
    public void InvalidExternalDeclaration_ShouldReportAtEachForUse(string constructor, string id)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;
            public class Service
            {
                {{constructor}}
            }
            """,
            assemblyName: "ExternalServices"
        ));
        const string source =
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver)
                {
                    resolver.For<Service>();
                    resolver.For<Service>();
                }
            }
            """;
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            source,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([id, id]));
        Assert.That(
            diagnostics.Select(diagnostic => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)),
            Is.All.EqualTo("Service")
        );
        Assert.That(diagnostics.SelectMany(diagnostic => diagnostic.AdditionalLocations), Is.Empty);
        Assert.That(sources.Length, Is.EqualTo(2));
        AssertCompiles(compilation);
    }

    [TestCase("[Assisted, Assisted] int")]
    [TestCase("[Assisted] Missing")]
    [TestCase("[Assisted(1)] ref int")]
    public void CompilerRejectedDeclaration_ShouldNotAddGeneratorDiagnostics(string parameter)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public Service({{parameter}} value) { }
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Service>();
                    resolver.For<Service>();
                }
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources.Length, Is.EqualTo(2));
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void GenericAssistedDeclarations_ShouldAllowInNullableAndArrayParameters()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable
            using Macaron.DependencyInjection;

            public class Service<T>
            {
                [Inject]
                public Service([Assisted] T first, [Assisted] in T second, [Assisted] T? third, [Assisted] params T[] values) { }
            }
            """
        );

        AssertCompiles(compilation);
    }
}
