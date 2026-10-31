using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class GenerationApiSelectionTests
{
    private const string StaticRegistrationMethodGroup =
        """
        System.Action<IDependencyRegistrar, object, System.Action<IService>> register =
            DependencyRegistrarExtensions.RegisterTransient<IService, Service>;

        register(registrar, null, null);
        """;

    private const string ReducedRegistrationMethodGroup =
        """
        System.Action<object, System.Action<IService>> register = registrar.RegisterTransient<IService, Service>;

        register(null, null);
        """;

    private const string InvalidConstraintMethodGroup =
        """
        System.Action<object, System.Action<Service>> register = registrar.RegisterTransient<Service, object>;
        """;

    private const string InvalidDelegateMethodGroup =
        """
        System.Action<int> register = registrar.RegisterTransient<object, Service>;
        """;

    private const string MarkedBaseMethod =
        """
        [Inject] public void Initialize() { }
        """;

    private const string AssistedBaseConstructor =
        """
        public Base() { }

        [Inject] public Base([Assisted] int value) { }
        """;

    [TestCase("registrar.Register<IService, Service>(Lifetime.Transient);")]
    [TestCase("registrar.RegisterTransient<IService, Service>();")]
    [TestCase("registrar.RegisterScoped<IService, Service>();")]
    [TestCase("registrar.RegisterSingleton<IService, Service>();")]
    [TestCase("DependencyRegistrarExtensions.RegisterTransient<IService, Service>(registrar);")]
    [TestCase("registrar?.RegisterTransient<IService, Service>();")]
    [TestCase(StaticRegistrationMethodGroup)]
    [TestCase(ReducedRegistrationMethodGroup)]
    public void AutomaticRegistration_ShouldIgnoreServiceInterfaceInjectAndConstructImplementation(string registration)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public interface IService
            {
                [Inject] void Initialize();
            }

            public class Service : IService
            {
                public void Initialize() { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        {{registration}}
                    });

                    return container.Resolve<IService>() is Service;
                }
            }
            """
        );
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var compilation, out var diagnostics);
        var factories = driver
            .GetRunResult()
            .Results
            .Single()
            .GeneratedSources
            .Single(source => source.HintName.StartsWith("Factories."))
            .SourceText
            .ToString();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Analyze(compilation), Is.Empty);
        Assert.That(factories, Does.Contain("typeof(global::Service)").And.Not.Contain("typeof(global::IService)"));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase(MarkedBaseMethod)]
    [TestCase(AssistedBaseConstructor)]
    public void AutomaticRegistration_ShouldIgnoreServiceBaseInjectionAndConstructDerivedType(string declarations)
    {
        var (compilation, sources) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public class Base
            {
                {{declarations}}
            }

            public class Derived : Base { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Base, Derived>();
                    });

                    return container.Resolve<Base>() is Derived;
                }
            }
            """
        );
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Contain("typeof(global::Derived)").And.Not.Contain("typeof(global::Base)"));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("[Inject] private void Initialize() { }")]
    [TestCase("[Inject] private Service(int value) { }")]
    [TestCase("[Inject] private Service([Assisted] int value) { }")]
    [TestCase("[Inject] public static int Initialize() => 1;")]
    [TestCase("public void Initialize([Tag] int value) { }")]
    [TestCase("[Inject] public Service([Assisted] ref int value) { }")]
    public void ExplicitRegistrations_ShouldIgnoreExternalInjectionDeclarationsAndResolveServices(string declaration)
    {
        var external = EmitAssembly(CreateCompilation(
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

            public static class Probe
            {
                public static bool Run()
                {
                    var instance = new Service();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Service>(
                            (IDependencyResolver resolver, ref CleanupCollector collector) => new Service()
                        );
                        registrar.RegisterScoped(() => new Service(), tag: "factory");
                        registrar.RegisterInstance(instance, tag: "instance");
                    });

                    return container.Resolve<Service>() is Service
                        && container.Resolve<Service>(tag: "factory") is Service
                        && object.ReferenceEquals(container.Resolve<Service>(tag: "instance"), instance);
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Analyze(input), Is.Empty);

        var (compilation, sources) = Generate(input);
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("factories.Add("));
        Assert.That(Execute<bool>(compilation, external), Is.True);
    }

    [Test]
    public void AutomaticRegistration_ShouldIgnoreInvalidExternalServiceDeclarations()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public interface IService
            {
                [Inject] void First();
                [Inject] void Second();
            }

            public class Service : IService
            {
                public void First() { }
                public void Second() { }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, sources) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IService, Service>();
                    });

                    return container.Resolve<IService>() is Service;
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Contain("typeof(global::Service)"));
        Assert.That(Execute<bool>(compilation, external), Is.True);
    }

    [TestCase("[Inject] public static int Initialize() => 1;", "MADI0015")]
    [TestCase("[Inject] private void Initialize() { }", "MADI0018")]
    [TestCase("public void Initialize([Tag] int value) { }", "MADI0011")]
    [TestCase("public Service([Assisted] int value) { }", "MADI0011")]
    public void ExplicitRegistration_ShouldRetainIndependentSourceDeclarationDiagnostics(string declaration, string expectedId)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }

                {{declaration}}
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterInstance(new Service());
                    registrar.RegisterTransient(() => new Service());
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]));
        AssertCompiles(compilation);
    }

    [TestCase("registrar.RegisterTransient<object, Service>(cleanup: 42);")]
    [TestCase("registrar.RegisterTransient<object, Service>(factory: () => new Service());")]
    [TestCase(InvalidConstraintMethodGroup)]
    [TestCase(InvalidDelegateMethodGroup)]
    public void CompilerRejectedRegistration_ShouldNotRequestGenerationOrReportInjectionDiagnostics(string registration)
    {
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public void Initialize() { }
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
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();
        var compilerErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factories, Does.Not.Contain("factories.Add("));
        Assert.That(compilerErrors, Is.Not.Empty);
    }

    [Test]
    public void SameNamedUserExtensions_ShouldNotRequestGenerationOrValidateExternalDeclarations()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public abstract class Service
            {
                [Inject] private void Initialize() { }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, sources) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class UserExtensions
            {
                public static void RegisterTransient<T>(this IDependencyRegistrar registrar, int marker) { }
                public static void Register<T>(this IDependencyRegistrar registrar, int marker) { }
                public static void For<T>(this IDependencyResolver resolver, int marker) { }
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<Service>(marker: 1);
                    UserExtensions.Register<Service>(registrar, marker: 1);
                    resolver.For<Service>(marker: 1);
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("factories.Add("));
        Assert.That(sources.Any(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.False);
        AssertCompiles(compilation);
    }
}
