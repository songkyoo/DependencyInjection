using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class MethodInjectionDiagnosticTests
{
    [TestCase("resolver.For<Service>().Create();", "Create")]
    [TestCase("var context = resolver.For<Service>(); context.Create();", "Create")]
    [TestCase("System.Func<Service> create = resolver.For<Service>().Create;", "Create")]
    public void MethodPathWithCreate_ShouldDiagnoseEntryPointWithoutGeneratingConstructor(string call, string locationText)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { [Inject] public void Initialize() { } }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { {{call}} }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0017"]));
        Assert.That(
            diagnostics.Single().Location.SourceTree!.GetText().ToString(diagnostics.Single().Location.SourceSpan),
            Is.EqualTo(locationText)
        );
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("InjectionContextExtensions.")).SourceText.ToString(),
            Does.Contain("void Inject(").And.Not.Contain(" Create(")
        );
        Assert.That(
            compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.True
        );
    }

    [TestCase("resolver.For<Service>().Inject(service);")]
    [TestCase("var context = resolver.For<Service>(); context.Inject(service);")]
    public void ConstructorPathWithInject_ShouldDiagnoseEntryPoint(string call)
    {
        var (_, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver, Service service) { {{call}} }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0017"]));
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("InjectionContextExtensions.")).SourceText.ToString(),
            Does.Contain(" Create(").And.Not.Contain("void Inject(")
        );
    }

    [TestCase("private")]
    [TestCase("protected")]
    [TestCase("private protected")]
    public void InaccessibleMethod_ShouldBlockTargetWithoutAffectingOtherTargets(string accessibility)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { [Inject] {{accessibility}} void Initialize() { } }

            public class Valid { [Inject] public void Initialize() { } }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver)
                {
                    resolver.For<Service>();
                    resolver.For<Service>();
                    resolver.For<Valid>();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018"]));
        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
        AssertCompiles(compilation);
    }

    [TestCase("container.For<Service>().Inject(service);", "CS7036")]
    [TestCase("container.For<Service>().Inject(service, 1, 2);", "CS1501")]
    [TestCase("container.For<Service>().Inject(service, \"wrong\");", "CS1503")]
    [TestCase("container.For<Service>().Inject(service, unknown: 1);", "CS1739")]
    public void IncorrectAssistedArguments_ShouldRemainCompilerErrors(string call, string expectedId)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { [Inject] public void Initialize([Assisted] int value) { } }

            public class Configuration
            {
                public void Configure(IDependencyResolver container, Service service) { {{call}} }
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Id),
            Does.Contain(expectedId)
        );
    }

    [TestCase("container.For<State>().Inject(state);")]
    [TestCase("container.For<State>().Inject(ref new State());")]
    public void StructTargetWithoutWritableRef_ShouldRemainCompilerError(string call)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public struct State { [Inject] public void Initialize() { } }

            public class Configuration
            {
                public void Configure(IDependencyResolver container, State state) { {{call}} }
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void ExternalPrivateMethod_WithInjectCall_ShouldReportAccessibilityWithoutFallback()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service { [Inject] private void Initialize() { } }
            """,
            assemblyName: "ExternalServices"
        ));
        var (_, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>().Inject(new Service()); }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018"]));
    }

    [Test]
    public void UserExtensionWithSameName_ShouldRemainOutsideLibraryEntryPointDiagnostics()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;
            public class Service { [Inject] public void Initialize() { } }
            public static class UserExtensions
            {
                public static int Create(this InjectionContext<Service> context) => 42;
            }
            public class Configuration
            {
                public int Configure(IDependencyResolver resolver) => resolver.For<Service>().Create();
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
        AssertCompiles(compilation);
    }

}
