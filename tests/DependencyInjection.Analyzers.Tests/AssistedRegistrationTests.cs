using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class AssistedRegistrationTests
{
    private const string ResolverTransientRegistration =
        """
        registrar.RegisterTransient(
            (IDependencyResolver resolver, ref CleanupCollector collector) => new Service(1)
        );
        """;

    private const string ResolverLifetimeRegistration =
        """
        registrar.Register<Service>(
            (IDependencyResolver resolver, ref CleanupCollector collector) => new Service(1),
            Lifetime.Transient
        );
        """;

    private const string ServiceDeclaration =
        """
        using Macaron.DependencyInjection;

        public interface IService { }

        public class Service : IService
        {
            public int Value { get; }
            public Service() { }

            [Inject]
            public Service([Assisted] int value) { Value = value; }
        }

        public class Derived : Service { }
        public class Valid { }
        """;

    [TestCase("registrar.RegisterTransient<Service>();", "Service", 1)]
    [TestCase("registrar.RegisterScoped<Service>();", "Service", 1)]
    [TestCase("registrar.RegisterSingleton<Service>();", "Service", 1)]
    [TestCase("registrar.Register<Service>(Lifetime.Transient);", "Service", 1)]
    [TestCase("registrar.RegisterTransient<object, Service>();", "Service", 1)]
    [TestCase("registrar.RegisterScoped<object, Service>();", "Service", 1)]
    [TestCase("registrar.RegisterSingleton<object, Service>();", "Service", 1)]
    [TestCase("registrar.Register<object, Service>(Lifetime.Transient);", "Service", 1)]
    [TestCase("registrar.RegisterTransient<Service, Service>();", "Service", 1)]
    [TestCase("registrar.RegisterTransient(cleanup: (Service value) => { });", "RegisterTransient", 1)]
    [TestCase("registrar?.RegisterTransient<Service>();", "Service", 1)]
    [TestCase("DependencyRegistrarExtensions.RegisterTransient<Service>(registrar);", "Service", 1)]
    [TestCase("Action<object, Action<Service>> register = registrar.RegisterTransient<Service>;", "Service", 1)]
    public void AssistedConstructor_ShouldBeRejectedOnlyAtAutomaticRegistrationApis(string registration, string locationText, int expectedCount)
    {
        var consumer =
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    {{registration}}
                    registrar.RegisterTransient<Valid>();
                }
            }

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    return container.For<Service>().Create(42).Value;
                }
            }
            """;
        var input = CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            text: ServiceDeclaration,
            options: InputParseOptions,
            path: "Service.cs"
        ));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(input);
        var locationTexts = diagnostics.Select(diagnostic =>
        {
            var span = diagnostic.Location.SourceSpan;

            return consumer.Substring(span.Start, span.Length);
        });

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("MADI0014", expectedCount))
        );
        Assert.That(locationTexts, Is.All.EqualTo(locationText));
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Contain("typeof(global::Valid)").And.Not.Contain("typeof(global::Service)").And.Not.Contain("typeof(global::Derived)")
        );
        Assert.That(Execute<int>(compilation), Is.EqualTo(42));
    }

    [TestCase("registrar.RegisterTransient<Service>(() => new Service(1));")]
    [TestCase("registrar.RegisterScoped<Service>(() => new Service(1));")]
    [TestCase("registrar.RegisterSingleton<Service>(() => new Service(1));")]
    [TestCase("registrar.Register<Service>(() => new Service(1), Lifetime.Transient);")]
    [TestCase("registrar.RegisterSingleton<Service>(new Service(1));")]
    [TestCase("registrar.RegisterSingleton(new Service(1));")]
    [TestCase("registrar.RegisterTransient(() => new Service(1));")]
    [TestCase("registrar.RegisterScoped(() => new Service(1));")]
    [TestCase("registrar.RegisterSingleton(() => new Service(1));")]
    [TestCase("IDependencyRegistrarExtensions.RegisterTransient<Service>(registrar, () => new Service(1));")]
    [TestCase(ResolverTransientRegistration)]
    [TestCase(ResolverLifetimeRegistration)]
    [TestCase("IDependencyRegistrarExtensions.RegisterSingleton(registrar, new Service(1));")]
    [TestCase("registrar.RegisterSingleton<Service>(() => resolver.For<Service>().Create(1));")]
    [TestCase("registrar.RegisterSingleton(() => resolver.For<Service>().Create(1));")]
    public void AssistedConstructor_ShouldAllowExplicitFactoryAndInstanceRegistrations(string registration)
    {
        var consumer =
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var resolver = DependencyContainerFactory.Create(_ => { });
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        {{registration}}
                    });

                    return container.Resolve<Service>().Value == 1;
                }
            }
            """;
        var input = CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            text: ServiceDeclaration,
            options: InputParseOptions,
            path: "Services.cs"
        ));
        var (compilation, sources) = Generate(input);
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("typeof(global::Service)"));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("RegisterTransient")]
    [TestCase("RegisterScoped")]
    [TestCase("RegisterSingleton")]
    public void CoreFactoryRegistration_ShouldAllowAssistedType(string method)
    {
        var consumer =
            $$"""
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.{{method}}<Service>((IDependencyResolver resolver, ref CleanupCollector collector) => new Service(1));
                }
            }
            """;
        var input = CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            text: ServiceDeclaration,
            options: InputParseOptions,
            path: "Service.cs"
        ));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(input);

        Assert.That(diagnostics, Is.Empty);
        AssertCompiles(compilation);
    }

    [TestCase("registrar.RegisterSingleton<Service>(factory: 42);")]
    [TestCase("registrar.RegisterSingleton<Service>(() => \"wrong\");")]
    [TestCase("registrar.RegisterTransient<Missing>();")]
    public void CompilerRejectedRegistration_ShouldNotAddAssistedDiagnostic(string registration)
    {
        var input = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    {{registration}}
                }
            }
            """
        ).AddSyntaxTrees(CSharpSyntaxTree.ParseText(ServiceDeclaration, InputParseOptions, "Service.cs"));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(input);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.True
        );
    }

    [Test]
    public void ExternalPrivateAssistedConstructor_ShouldRejectForWithoutConstructorFallback()
    {
        const string declaration =
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }
                [Inject] private Service([Assisted] string value) { }
            }
            """;
        var external = EmitAssembly(CreateCompilation(declaration, "ExternalServices"));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver)
                {
                    resolver.For<Service>();
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0018" }));
        AssertCompiles(compilation);
    }

    [TestCase("public Service([Assisted] int value) { }", "MADI0011")]
    [TestCase("[Inject] public Service([Assisted] ref int value) { }", "MADI0008")]
    [TestCase("[Inject] public Service([Tag, Assisted] int value) { }", "MADI0013")]
    public void ExternalDeclarationErrors_ShouldBeCheckedDuringAutomaticRegistration(
        string constructor,
        params string[] expectedIds
    )
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }

                {{constructor}}
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterSingleton<Service>();
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(expectedIds));
        Assert.That(diagnostics.SelectMany(diagnostic => diagnostic.AdditionalLocations), Is.Empty);
        AssertCompiles(compilation);
    }

    [Test]
    public void InterfaceAndAbstractRegistrations_ShouldNotInferAssistedRuntimeImplementation()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public abstract class AbstractService { }

            public class Implementation : AbstractService
            {
                [Inject] public Implementation([Assisted] int value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IService>(new Service(1));
                        registrar.RegisterTransient<AbstractService>(() => new Implementation(2));
                    });

                    return container.Resolve<IService>() is Service && container.Resolve<AbstractService>() is Implementation;
                }
            }
            """
        ).AddSyntaxTrees(CSharpSyntaxTree.ParseText(ServiceDeclaration, InputParseOptions, "Service.cs"));
        var (compilation, _) = Generate(input);

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void SameNamedApisAndAttributes_ShouldRemainOutsideAssistedRegistrationRules()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class AssistedAttribute : System.Attribute { }

            public class Ordinary
            {
                public Ordinary([Assisted] string value) { }
            }

            public class Service
            {
                [Inject] public Service([Macaron.DependencyInjection.Assisted] int value) { }
            }

            public class FakeRegistrar
            {
                public void RegisterSingleton<T>(T value) { }
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, FakeRegistrar fake)
                {
                    fake.RegisterSingleton(new Service(1));
                    registrar.RegisterTransient<Ordinary>();
                }
            }
            """
        );

        AssertCompiles(compilation);
    }
}
