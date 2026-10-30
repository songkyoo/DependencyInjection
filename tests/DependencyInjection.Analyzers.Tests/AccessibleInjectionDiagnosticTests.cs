using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class AccessibleInjectionDiagnosticTests
{
    [TestCase("[Inject] public Service() { } [Inject] internal Service(int value) { }")]
    [TestCase("[Inject] public Service() { } [Inject] internal void Initialize() { }")]
    [TestCase("[Inject] public void First() { } [Inject] internal void Second() { }")]
    public void DuplicateInjectDeclarations_IncludingPrivateMember_ShouldUseOneDiagnostic(string declarations)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                {{declarations}}
                [Inject] private static int Hidden() => 1;
            }
            """
        );
        var diagnostics = Analyze(input);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0005"]));
        Assert.That(diagnostics.Single().AdditionalLocations, Has.Count.EqualTo(2));
        AssertCompiles(input);
    }

    [Test]
    public void ExternalInternalInjectMethod_WithFriendAccess_ShouldRejectExplicitRegistrationUsingDefaultMetadata()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]

            public class Service { [Inject] internal void Initialize() { } }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar) { registrar.RegisterSingleton(new Service()); }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var diagnostics = Analyze(input);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0016"]));
        AssertCompiles(input);
    }

    [Test]
    public void RegistrationInsideCompiledAsyncMethod_ShouldNotRequestBodyDiagnosticsAgain()
    {
        var input = CreateCompilation(
            """
            using System.Threading.Tasks;
            using Macaron.DependencyInjection;

            public class Service { [Inject] public void Initialize() { } }

            public class Configuration
            {
                public async Task Configure(IDependencyRegistrar registrar)
                {
                    await Task.Yield();

                    registrar.RegisterSingleton(new Service());
                }
            }
            """
        );

        AssertCompiles(input);

        var diagnostics = Analyze(input);

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0016"]),
            string.Join(Environment.NewLine, diagnostics)
        );
    }

    [Test]
    public void CompilerInvalidRegistrationConstraints_ShouldNotProduceRegistrationDiagnostic()
    {
        var (_, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service { [Inject] public void Initialize() { } }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar) { registrar.Register<Service, object>(Lifetime.Transient); }
            }
            """
        );

        Assert.That(diagnostics, Is.Empty);
    }
}
