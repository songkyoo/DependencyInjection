using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class ConstructorDiagnosticTests
{
    private const string DuplicateConstructorDeclarations =
        """
        [Inject] public Service() { }

        [Inject] public Service() { }
        """;

    private const string MultipleInjectConstructors =
        """
        public class Service
        {
            [Inject] public Service() { }
            [Inject] public Service(int value) { }
        }
        """;

    private const string AmbiguousConstructors =
        """
        public class Service
        {
            public Service() { }
            public Service(int value) { }
        }
        """;

    [TestCase("T", "", "MADI0001")]
    [TestCase("Service<T>", "public class Service<T> { }", "MADI0001")]
    [TestCase("Service", "public abstract class Service { }", "MADI0003")]
    [TestCase("Service", "public interface Service { }", "MADI0003")]
    [TestCase("Service", "public enum Service { Value }", "MADI0003")]
    [TestCase("int[]", "", "MADI0003")]
    [TestCase("Service", "private class Service { }", "MADI0004")]
    [TestCase("Service", "public class Service { private Service() { } }", "MADI0006")]
    [TestCase("Service", AmbiguousConstructors, "MADI0007")]
    [TestCase("Service", "public struct Service { public Service(int value) { } }", "MADI0007")]
    [TestCase("Service", "public class Service { public Service(System.Span<int> value) { } }", "MADI0008")]
    [TestCase("Service", "public class Service { public Service(ref int value) { } }", "MADI0008")]
    [TestCase("Service", "public class Service { public Service(out int value) { value = 0; } }", "MADI0008")]
    [TestCase("Service", "public class Service { public Service(dynamic value) { } }", "MADI0002")]
    [TestCase("Service", "public class Service { public Service(dynamic[] value) { } }", "MADI0002")]
    [TestCase("Service", "public class Service { public Service(System.Collections.Generic.List<dynamic> value) { } }", "MADI0002")]
    [TestCase("Outer<T>.Service", "public class Outer<T> { public class Service { } }", "MADI0001")]
    [TestCase("Service<dynamic>", "public class Service<T> { }", "MADI0002")]
    public void InvalidTarget_ShouldDiagnoseEachUseAndPreserveValidConstruction(string target, string declaration, string expectedId)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public class Valid { }

            public class Configuration
            {
                {{declaration}}

                public void Configure<T>(IDependencyRegistrar registrar, IDependencyResolver resolver) where T : notnull
                {
                    registrar.RegisterTransient<{{target}}>();
                    registrar.RegisterScoped<object, {{target}}>();
                    resolver.For<{{target}}>();
                    resolver.For<{{target}}>();

                    registrar.RegisterTransient<Valid>();
                    resolver.For<Valid>().Create();
                }
            }
            """;
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(source);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(Enumerable.Repeat(expectedId, 4)));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Severity), Is.All.EqualTo(DiagnosticSeverity.Error));
        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan.Start).Distinct().Count(),
            Is.EqualTo(4)
        );
        Assert.That(
            diagnostics.Select(diagnostic => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)),
            Is.All.EqualTo(target)
        );
        Assert.That(
            diagnostics.Last().Location.SourceSpan.Start,
            Is.EqualTo(source.LastIndexOf($"For<{target}>", StringComparison.Ordinal) + "For<".Length)
        );
        Assert.That(diagnostics.Select(diagnostic => diagnostic.GetMessage()), Is.All.Contains(target));
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Contain("typeof(global::Valid)").And.Contain("capacity: 1")
        );
        AssertCompiles(compilation);
    }

    [Test]
    public void DuplicateAccessibleInjectConstructors_ShouldDiagnoseDeclarationOnceAndKeepValidTarget()
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            {{MultipleInjectConstructors}}

            public class Valid { }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Service>();
                    resolver.For<Service>();
                    resolver.For<Service>();
                    resolver.For<Valid>().Create();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0005"]));
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
        AssertCompiles(compilation);
    }

    [TestCase("Missing")]
    [TestCase("System.Span<int>")]
    public void CompilerRejectedTarget_ShouldNotProduceConstructionDiagnostics(string target)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<{{target}}>();
                    registrar.RegisterTransient<object, {{target}}>();
                    resolver.For<{{target}}>();
                }
            }
            """
        );

        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources, Has.Length.EqualTo(2));
    }

    [TestCase("public Service(Missing value) { }")]
    [TestCase("[Inject, Inject] public Service(in int value) { }")]
    [TestCase(DuplicateConstructorDeclarations)]
    [TestCase("[Inject] public Service([Tag, Tag] int value) { }")]
    public void CompilerRejectedConstructor_ShouldNotProduceConstructionDiagnostics(string constructor)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{constructor}}
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Service>();
                    registrar.RegisterTransient<object, Service>();
                    resolver.For<Service>();
                }
            }
            """
        );

        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources, Has.Length.EqualTo(2));
    }
}
