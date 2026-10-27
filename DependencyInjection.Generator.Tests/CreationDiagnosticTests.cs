using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationDiagnosticTests
{
    [TestCase("T", "", "MADI0001")]
    [TestCase("Service<T>", "public class Service<T> { }", "MADI0001")]
    [TestCase("Outer<T>.Service", "public class Outer<T> { public class Service { } }", "MADI0001")]
    [TestCase("Service<dynamic>", "public class Service<T> { }", "MADI0002")]
    [TestCase("Service", "public abstract class Service { }", "MADI0003")]
    [TestCase("Service", "public interface Service { }", "MADI0003")]
    [TestCase("int[]", "", "MADI0003")]
    [TestCase("Service", "private class Service { }", "MADI0004")]
    [TestCase("Service", "public class Service { [Inject] public Service() { } [Inject] public Service(int value) { } }", "MADI0005")]
    [TestCase("Service", "public class Service { private Service() { } }", "MADI0006")]
    [TestCase("Service", "public class Service { public Service() { } public Service(int value) { } }", "MADI0007")]
    [TestCase("Service", "public class Service { [Inject] private Service() { } }", "MADI0008")]
    [TestCase("Service", "public class Service { public Service(ref int value) { } }", "MADI0009")]
    [TestCase("Service", "public class Service { public Service(out int value) { value = 0; } }", "MADI0009")]
    [TestCase("Service", "public class Service { public Service(System.Span<int> value) { } }", "MADI0009")]
    [TestCase("Service", "public class Service { public Service(dynamic value) { } }", "MADI0002")]
    public void InvalidForTarget_ShouldDiagnoseUseAndPreserveOtherCreation(string target, string declaration, string id)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public class Valid { }

            public class Configuration
            {
                {{declaration}}
                public void Configure<T>(IDependencyResolver resolver)
                {
                    resolver.For<Valid>().Create();
                    resolver.For<{{target}}>();
                    resolver.For<{{target}}>();
                }
            }
            """;
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(source);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { id, id }));
        Assert.That(diagnostics.All(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
        Assert.That(diagnostics.Last().Location.SourceSpan.Start, Is.EqualTo(source.LastIndexOf(target, StringComparison.Ordinal)));
        Assert.That(sources.Count(result => result.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
        AssertCompiles(compilation);
    }

    [TestCase("Missing")]
    [TestCase("System.Span<int>")]
    public void CompilerRejectedFor_ShouldNotDuplicateDiagnostic(string target)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<{{target}}>(); }
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [TestCase("public Service(Missing value) { }")]
    [TestCase("[Inject, Inject] public Service() { }")]
    [TestCase("[Inject] public Service([Tag, Tag] int value) { }")]
    public void CompilerRejectedConstructor_ShouldNotDuplicateDiagnostic(string constructor)
    {
        var (_, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{constructor}}
            }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(sources, Has.Length.EqualTo(2));
    }

    [TestCase("public Service([Tag] int value) { }", "MADI0012")]
    [TestCase("[Inject] public Service([Tag(new[] { 1 })] int value) { }", "MADI0011")]
    [TestCase("[Inject] public Service([Tag] ref int value) { }", "MADI0009")]
    [TestCase("[Inject] public Service([Tag] dynamic value) { }", "MADI0002")]
    [TestCase("[Inject] private Service() { }", "MADI0008")]
    public void InvalidExternalConstructor_ShouldDiagnoseForLocation(string constructor, string id)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{constructor}}
            }
            """,
            assemblyName: "External"
        ));
        const string source =
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """;
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(source, additionalReferences: MetadataReference.CreateFromImage(external));
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo(id));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.LastIndexOf("Service", StringComparison.Ordinal)));
        Assert.That(diagnostic.AdditionalLocations, Is.Empty);
        Assert.That(sources, Has.Length.EqualTo(2));
        AssertCompiles(compilation);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExternalInternalConstructor_ShouldUseConsumerFriendAccess(bool friend)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            {{(friend ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Consumer\")]" : "")}}
            public class Service
            {
                public string Value;

                [Inject] internal Service([Tag("value")] string value) { Value = value; }
            }
            """,
            assemblyName: "External"
        ));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;
            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(r => r.RegisterSingleton(instance: "expected", tag: "value"));
                    var context = container.For<Service>();
            """ + (friend ? "return context.Create().Value == \"expected\";" : "return true;") +
            """
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(friend ? Array.Empty<string>() : ["MADI0008"])
        );
        Assert.That(Execute<bool>(compilation, external), Is.True);
    }

    [TestCase("public Invalid([Tag] int value) { }", "MADI0012")]
    [TestCase("[Inject] public Invalid([Tag(new[] { 1 })] int value) { }", "MADI0011")]
    [TestCase("[Inject] public Invalid([Tag] ref int value) { }", "MADI0009")]
    public void InvalidSourceDeclaration_ShouldDiagnoseOnceAcrossRepeatedForUses(string constructor, string id)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Invalid { {{constructor}} }

            public class Valid { }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver)
                {
                    resolver.For<Invalid>(); resolver.For<Invalid>();
                    resolver.For<Valid>().Create();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([id]));
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
        AssertCompiles(compilation);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExternalTagTypeAccessibility_ShouldFollowConsumerFriendAccess(bool friend)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            {{(friend ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Consumer\")]" : "")}}
            internal enum Kind { Value }

            public class Service
            {
                [Inject] public Service([Tag(Kind.Value)] int value) { }
            }
            """,
            assemblyName: "External"
        ));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(friend ? Array.Empty<string>() : ["MADI0013"])
        );
        Assert.That(sources.Length, Is.EqualTo(friend ? 3 : 2));
        AssertCompiles(compilation);
    }
}
