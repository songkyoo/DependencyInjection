using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Analyzers.Tests;

public class MethodInjectionRegistrationTests
{
    private const string ResolverTransientRegistration =
        """
        registrar.RegisterTransient(
            (IDependencyResolver resolver, ref CleanupCollector collector) => new Service()
        );
        """;

    private const string ResolverLifetimeRegistration =
        """
        registrar.Register<Service>(
            (IDependencyResolver resolver, ref CleanupCollector collector) => new Service(),
            Lifetime.Transient
        );
        """;

    private const string Services =
        """
        using Macaron.DependencyInjection;

        public interface IService { }
        public class Service : IService
        {
            [Inject] public void Initialize() { }
        }
        public class Derived : Service { }
        public class Valid { }
        """;

    [TestCase("registrar.RegisterTransient<Service>();", "Service")]
    [TestCase("registrar.RegisterScoped<Service>();", "Service")]
    [TestCase("registrar.RegisterSingleton<Service>();", "Service")]
    [TestCase("registrar.Register<Service>(Lifetime.Transient);", "Service")]
    [TestCase("registrar.RegisterTransient<object, Service>();", "Service")]
    [TestCase("registrar.RegisterScoped<object, Service>();", "Service")]
    [TestCase("registrar.RegisterSingleton<object, Service>();", "Service")]
    [TestCase("registrar.Register<object, Service>(Lifetime.Singleton);", "Service")]
    [TestCase("registrar.RegisterTransient<Service, Service>();", "Service")]
    [TestCase("registrar.RegisterTransient(cleanup: (Service value) => { });", "RegisterTransient")]
    [TestCase("registrar?.RegisterTransient<Service>();", "Service")]
    [TestCase("DependencyRegistrarExtensions.RegisterTransient<Service>(registrar);", "Service")]
    [TestCase("Action<object, Action<Service>> register = registrar.RegisterTransient<Service>;", "Service")]
    public void MethodPath_ShouldBeRejectedOnlyAtAutomaticRegistrationApis(string registration, string locationText)
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
        var input = CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            text: Services,
            options: InputParseOptions,
            path: "Services.cs"
        ));
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(input);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0016"]));
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

    [TestCase("registrar.RegisterTransient<Service>(() => new Service());")]
    [TestCase("registrar.RegisterScoped<Service>(() => new Service());")]
    [TestCase("registrar.RegisterSingleton<Service>(() => new Service());")]
    [TestCase("registrar.Register<Service>(() => new Service(), Lifetime.Transient);")]
    [TestCase("registrar.RegisterInstance<Service>(new Service());")]
    [TestCase("registrar.RegisterInstance(new Service());")]
    [TestCase("registrar.RegisterTransient(() => new Service());")]
    [TestCase("registrar.RegisterScoped(() => new Service());")]
    [TestCase("registrar.RegisterSingleton(() => new Service());")]
    [TestCase("IDependencyRegistrarExtensions.RegisterTransient<Service>(registrar, () => new Service());")]
    [TestCase(ResolverTransientRegistration)]
    [TestCase(ResolverLifetimeRegistration)]
    public void MethodPath_ShouldAllowExplicitFactoryAndInstanceRegistrations(string registration)
    {
        var consumer =
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        {{registration}}
                    });

                    return container.Resolve<Service>() is Service;
                }
            }
            """;
        var input = CreateCompilation(consumer).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            text: Services,
            options: InputParseOptions,
            path: "Services.cs"
        ));
        var (compilation, sources) = Generate(input);
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("typeof(global::Service)"));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ExplicitRegistrations_ShouldUseStaticTypeAndIgnoreInheritedDeclarationsAndFactoryBodies()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public abstract class AbstractService { }

            public class Implementation : AbstractService
            {
                [Inject] public void Initialize() { }
            }

            public class OtherRegistrar
            {
                public void RegisterSingleton<T>(T value) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance<IService>(new Service());
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
    public void GeneratorWithoutAnalyzer_ShouldNotProduceFactoryForMethodPath()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public void Initialize() { }
            }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar) { registrar.RegisterTransient<Service>(); }
            }
            """
        );
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var source = driver
            .GetRunResult()
            .Results
            .Single()
            .GeneratedSources
            .Single(source => source.HintName.StartsWith("Factories."));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(source.SourceText.ToString(), Does.Not.Contain("typeof(global::Service)"));
        AssertCompiles(output);
    }
}
