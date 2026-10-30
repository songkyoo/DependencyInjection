using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class MethodInjectionRegistrationTests
{
    private const string Services =
        """
        using Macaron.DependencyInjection;

        public interface IService { }
        public class Service : IService { [Inject] public void Initialize() { } }
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
    [TestCase("registrar.Register<object, Service>(Lifetime.Singleton);", "Service", 1)]
    [TestCase("registrar.RegisterTransient<Service, Service>();", "Service", 2)]
    [TestCase("registrar.RegisterTransient<Service, Derived>();", "Service", 1)]
    [TestCase("registrar.RegisterTransient<Service>(() => new Service());", "Service", 1)]
    [TestCase("registrar.RegisterScoped<Service>(() => new Service());", "Service", 1)]
    [TestCase("registrar.RegisterSingleton<Service>(() => new Service());", "Service", 1)]
    [TestCase("registrar.Register<Service>(() => new Service(), Lifetime.Transient);", "Service", 1)]
    [TestCase("registrar.RegisterSingleton<Service>(new Service());", "Service", 1)]
    [TestCase("registrar.RegisterSingleton(new Service());", "RegisterSingleton", 1)]
    [TestCase("registrar.RegisterTransient(() => new Service());", "RegisterTransient", 1)]
    [TestCase("registrar.RegisterScoped(() => new Service());", "RegisterScoped", 1)]
    [TestCase("registrar.RegisterSingleton(() => new Service());", "RegisterSingleton", 1)]
    [TestCase("registrar.RegisterTransient(cleanup: (Service value) => { });", "RegisterTransient", 1)]
    [TestCase("registrar?.RegisterTransient<Service>();", "Service", 1)]
    [TestCase("DependencyRegistrarExtensions.RegisterTransient<Service>(registrar);", "Service", 1)]
    [TestCase("IDependencyRegistrarExtensions.RegisterTransient<Service>(registrar, () => new Service());", "Service", 1)]
    [TestCase("Action<object, Action<Service>> register = registrar.RegisterTransient<Service>;", "Service", 1)]
    [TestCase("registrar.RegisterTransient((IDependencyResolver resolver, ref CleanupCollector collector) => new Service());", "RegisterTransient", 1)]
    [TestCase("registrar.Register<Service>((IDependencyResolver resolver, ref CleanupCollector collector) => new Service(), Lifetime.Transient);", "Service", 1)]
    public void MethodPath_ShouldBeRejectedAtEveryRegistrationApi(string registration, string locationText, int count)
    {
        var consumer =
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    {{registration}}
                    registrar.RegisterTransient<Valid>();
                }
            }
            """;
        var input = CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(Services, InputParseOptions, "Services.cs"));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(input);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(Enumerable.Repeat("MADI0016", count)));
        Assert.That(
            diagnostics.Select(diagnostic =>
            {
                return consumer.Substring(
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length
                );
            }),
            Is.All.EqualTo(locationText)
        );
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does
                .Contain("typeof(global::Valid)")
                .And.Not.Contain("typeof(global::Service)")
                .And.Not.Contain("typeof(global::Derived)")
        );
        AssertCompiles(compilation);
    }

    [Test]
    public void ExplicitRegistrations_ShouldUseStaticTypeAndIgnoreInheritedDeclarationsAndFactoryBodies()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public abstract class AbstractService { }

            public class Implementation : AbstractService { [Inject] public void Initialize() { } }

            public class OtherRegistrar { public void RegisterSingleton<T>(T value) { } }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IService>(new Service());
                        registrar.RegisterTransient<AbstractService>(() => new Implementation());
                        registrar.RegisterTransient<Derived>();
                    });
                    new OtherRegistrar().RegisterSingleton(new Service());

                    return container.Resolve<IService>() is Service
                        && container.Resolve<AbstractService>() is Implementation
                        && container.Resolve<Derived>() is Derived;
                }
            }
            """
        ).AddSyntaxTrees(CSharpSyntaxTree.ParseText(Services, InputParseOptions, "Services.cs"));
        var (compilation, _) = Generate(input);

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ExternalPrivateMethod_ShouldRejectForWithoutConstructorFallback()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service { [Inject] private void Initialize() { } }
            """,
            assemblyName: "ExternalServices"
        ));
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );
        var diagnostics = Analyze(input);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018"]));
        AssertCompiles(input);
    }

    [Test]
    public void GeneratorWithoutAnalyzer_ShouldNotProduceFactoryForMethodPath()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service { [Inject] public void Initialize() { } }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar) { registrar.RegisterTransient<Service>(); }
            }
            """
        );
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var source = driver.GetRunResult().Results.Single().GeneratedSources.Single(source => source.HintName.StartsWith("Factories."));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(source.SourceText.ToString(), Does.Not.Contain("typeof(global::Service)"));
        AssertCompiles(output);
    }
}
