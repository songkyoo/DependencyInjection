using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class AssistedRegistrationTests
{
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
    [TestCase("registrar.RegisterTransient<Service, Service>();", "Service", 2)]
    [TestCase("registrar.RegisterTransient<Service, Derived>();", "Service", 1)]
    [TestCase("registrar.RegisterTransient<Service>(() => new Service(1));", "Service", 1)]
    [TestCase("registrar.RegisterScoped<Service>(() => new Service(1));", "Service", 1)]
    [TestCase("registrar.RegisterSingleton<Service>(() => new Service(1));", "Service", 1)]
    [TestCase("registrar.Register<Service>(() => new Service(1), Lifetime.Transient);", "Service", 1)]
    [TestCase("registrar.RegisterSingleton<Service>(new Service(1));", "Service", 1)]
    [TestCase("registrar.RegisterSingleton(new Service(1));", "RegisterSingleton", 1)]
    [TestCase("registrar.RegisterTransient(() => new Service(1));", "RegisterTransient", 1)]
    [TestCase("registrar.RegisterScoped(() => new Service(1));", "RegisterScoped", 1)]
    [TestCase("registrar.RegisterSingleton(() => new Service(1));", "RegisterSingleton", 1)]
    [TestCase("registrar.RegisterTransient(cleanup: (Service value) => { });", "RegisterTransient", 1)]
    [TestCase("registrar?.RegisterTransient<Service>();", "Service", 1)]
    [TestCase("DependencyRegistrarExtensions.RegisterTransient<Service>(registrar);", "Service", 1)]
    [TestCase("IDependencyRegistrarExtensions.RegisterTransient<Service>(registrar, () => new Service(1));", "Service", 1)]
    [TestCase("Action<object, Action<Service>> register = registrar.RegisterTransient<Service>;", "Service", 1)]
    [TestCase("registrar.RegisterTransient((IDependencyResolver resolver, ref CleanupCollector collector) => new Service(1));", "RegisterTransient", 1)]
    [TestCase("registrar.Register<Service>((IDependencyResolver resolver, ref CleanupCollector collector) => new Service(1), Lifetime.Transient);", "Service", 1)]
    [TestCase("IDependencyRegistrarExtensions.RegisterSingleton(registrar, new Service(1));", "RegisterSingleton", 1)]
    [TestCase("registrar.RegisterSingleton<Service>(() => resolver.For<Service>().Create(1));", "Service", 1)]
    [TestCase("registrar.RegisterSingleton(() => resolver.For<Service>().Create(1));", "RegisterSingleton", 1)]
    public void AssistedConstructor_ShouldBeRejectedAtEachRegistrationApi(string registration, string locationText, int expectedCount)
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
        var input = CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(ServiceDeclaration, InputParseOptions, "Service.cs"));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(input);

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("MADI0015", expectedCount))
        );
        Assert.That(
            diagnostics.Select(diagnostic => consumer.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)),
            Is.All.EqualTo(locationText)
        );
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Contain("typeof(global::Valid)").And.Not.Contain("typeof(global::Service)").And.Not.Contain("typeof(global::Derived)")
        );
        Assert.That(Execute<int>(compilation), Is.EqualTo(42));
    }

    [TestCase("RegisterTransient")]
    [TestCase("RegisterScoped")]
    [TestCase("RegisterSingleton")]
    public void CoreFactoryRegistration_ShouldRejectAssistedType(string method)
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
        var input = CreateCompilation(consumer)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(ServiceDeclaration, InputParseOptions, "Service.cs"));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(input);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0015"]));
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
    public void ExternalPrivateAssistedConstructor_ShouldPreventExplicitInstanceRegistration()
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
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterSingleton(new Service());
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0015"]));
        Assert.That(diagnostics.Single().AdditionalLocations, Is.Empty);
        AssertCompiles(compilation);
    }

    [TestCase("private Service([Assisted] int value) { }", "MADI0012")]
    [TestCase("[Inject] private Service([Assisted] ref int value) { }", "MADI0009", "MADI0015")]
    [TestCase("[Inject] private Service([Tag, Assisted] int value) { }", "MADI0014", "MADI0015")]
    public void ExternalDeclarationErrors_ShouldBeCheckedDuringExplicitRegistration(string constructor, params string[] expectedIds)
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
                    registrar.RegisterSingleton(new Service());
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
            public class Ordinary { public Ordinary([Assisted] string value) { } }
            public class Service
            {
                [Inject] public Service([Macaron.DependencyInjection.Assisted] int value) { }
            }
            public class FakeRegistrar { public void RegisterSingleton<T>(T value) { } }
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
