using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class InjectionDeclarationSelectionTests
{
    private const string PrivateConstructorAndPublicMethod =
        """
        [Inject] private Service(int value) { }

        [Inject] public void Initialize() { }
        """;

    private const string PublicConstructorAndPrivateMethod =
        """
        [Inject] public Service(int value) { }

        [Inject] private void Initialize() { }
        """;

    private const string PrivateMethods =
        """
        [Inject] private void First() { }

        [Inject] private void Second() { }
        """;

    [TestCase("[Inject] private Service(int value) { }")]
    [TestCase("[Inject] private Service([Assisted] string value) { }")]
    [TestCase("[Inject] internal Service(int value) { }")]
    [TestCase("[Inject] protected Service(int value) { }")]
    [TestCase("[Inject] private void Initialize() { }")]
    [TestCase("[Inject] internal void Initialize() { }")]
    [TestCase("[Inject] protected void Initialize() { }")]
    public void ExternalInaccessibleInject_ShouldPreventFallbackToPublicConstructor(string declaration)
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }
                {{declaration}}
            }
            """, assemblyName: "ExternalServices"
        ));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
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
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018", "MADI0018"]));
        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan.Start).Distinct().Count(),
            Is.EqualTo(2)
        );
        Assert.That(sources.Any(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.False);
        AssertCompiles(compilation);
    }

    [TestCase(PrivateConstructorAndPublicMethod)]
    [TestCase(PublicConstructorAndPrivateMethod)]
    [TestCase(PrivateMethods)]
    public void ExternalDuplicateInject_IncludingInaccessibleMembers_ShouldDiagnoseBeforeAccessibility(
        string declarations
    )
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }

                {{declarations}}
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (_, sources, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0005"]));
        Assert.That(sources.Any(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.False);
    }

    [Test]
    public void ExternalMarkedInternalConstructor_WithFriendAccess_ShouldOverridePublicConstructor()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]
            public class Service<T>
            {
                public T Value;

                public Service() { }

                [Inject] internal Service(T value) { Value = value; }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static (int, int) Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: 42);
                        registrar.RegisterTransient<Service<int>>();
                    });

                    return (container.Resolve<Service<int>>().Value, container.For<Service<int>>().Create().Value);
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<(int, int)>(compilation, external), Is.EqualTo((42, 42)));
    }

}
