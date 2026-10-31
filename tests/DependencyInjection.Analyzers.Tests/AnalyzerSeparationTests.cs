using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class AnalyzerSeparationTests
{
    [TestCase("public Service([Tag(\"main\")] string value) { }", "MADI0011")]
    [TestCase("[Inject] public Service([Tag(new[] { 1 })] string value) { }", "MADI0010")]
    [TestCase("[Inject] public Service([Tag, Assisted] int value) { }", "MADI0013")]
    [TestCase("[Inject] public Service([Assisted] ref int value) { }", "MADI0008")]
    [TestCase("[Inject] public Service([Assisted] dynamic value) { }", "MADI0002")]
    public void AnalyzerWithoutGenerator_ShouldCheckUnusedDeclarations(string constructor, string expectedId)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{constructor}}
            }
            """
        );

        Assert.That(Analyze(input).Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]));
        Assert.That(input.GetTypeByMetadataName("Macaron.DependencyInjection.DependencyRegistrarExtensions"), Is.Null);
    }

    [TestCase("registrar.RegisterInstance(new Service(1));")]
    [TestCase("registrar.RegisterTransient(() => new Service(1));")]
    [TestCase("registrar.RegisterTransient<Service>((IDependencyResolver resolver, ref CleanupCollector collector) => new Service(1));")]
    [TestCase("IDependencyRegistrarExtensions.RegisterInstance(registrar, new Service(1));")]
    public void AnalyzerWithoutGenerator_ShouldAllowExplicitAssistedRegistrations(string registration)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public Service([Assisted] int value) { }
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    {{registration}}
                }
            }
            """
        );

        AssertCompiles(input);
        Assert.That(Analyze(input), Is.Empty);
    }

    [TestCase("[Inject] private Service([Assisted] int value) { }")]
    [TestCase("[Inject] private void Initialize() { }")]
    public void AnalyzerWithoutGenerator_ShouldRejectExternalPrivateInject(string declaration)
    {
        var image = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }
                {{declaration}}
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) => resolver.For<Service>();
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(image)
        );

        Assert.That(Analyze(input).Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018"]));
        AssertCompiles(input);
    }

    [Test]
    public void GeneratorWithoutAnalyzer_ShouldOmitInvalidFactoryAndLeaveDeclarationReportingToAnalyzer()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service([Tag("main")] string value) { }
            }

            public class Valid { }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service>();
                    registrar.RegisterTransient<Valid>();
                }
            }
            """
        );
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var factories = driver.GetRunResult().Results.Single().GeneratedSources
            .Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factories, Does.Not.Contain("typeof(global::Service)").And.Contain("typeof(global::Valid)"));
        Assert.That(Analyze(output).Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0011"]));
        AssertCompiles(output);
    }

    [Test]
    public void GeneratorWithoutAnalyzer_ShouldReportInvalidConstructionTarget()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public abstract class Service { }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) => resolver.For<Service>();
            }
            """
        );
        CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0003"]));
        Assert.That(Analyze(output), Is.Empty);
    }

    [Test]
    public void Analyzer_ShouldAllowExplicitFactoryUsingGeneratedCreate()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public Service([Assisted] int value) { }
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterSingleton(() => resolver.For<Service>().Create(1));
                }
            }
            """
        );
        CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Analyze(output), Is.Empty);
        AssertCompiles(output);
    }

    [Test]
    public void GeneratedDeclaration_ShouldSkipIndependentReportingAndStillPreventInvalidInjectionOutput()
    {
        var generated = CSharpSyntaxTree.ParseText(
            text:
                """
                // <auto-generated />
                using Macaron.DependencyInjection;

                public class GeneratedInvalid
                {
                    [Inject] public static void Initialize() { }
                }
                """,
            options: InputParseOptions,
            path: "OtherGenerator.g.cs"
        );
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Valid
            {
                [Inject] public void Initialize() { }
            }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver)
                {
                    resolver.For<GeneratedInvalid>();
                    resolver.For<Valid>();
                }
            }
            """
        ).AddSyntaxTrees(generated);
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(input);
        var contextSources = sources
            .Where(source => source.HintName.StartsWith("InjectionContextExtensions."))
            .ToArray();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(contextSources, Has.Length.EqualTo(1));
        Assert.That(contextSources.Single().SourceText.ToString(), Does.Contain("InjectionContext<global::Valid>"));
        AssertCompiles(compilation);
    }
}
