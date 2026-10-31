using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class SingleTypeRegistrationTests
{
    private const string RegistrationMethodGroup =
        """
        System.Action<IDependencyRegistrar, object, System.Action<Service>> register =
            Registrar.RegisterTransient<Service>;

        register(registrar, null, null);
        """;

    [TestCase("Register", "Lifetime.Transient")]
    [TestCase("RegisterTransient", "")]
    [TestCase("RegisterScoped", "")]
    [TestCase("RegisterSingleton", "")]
    public void FactoryOmittedRegistration_ShouldSelectInjectConstructorAndResolveDependencies(string method, string arguments)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string Value { get; }

                public Service() { Value = "default"; }

                [Inject] internal Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: "injected");
                        registrar.{{method}}<Service>({{arguments}});
                    });

                    return container.Resolve<Service>().Value == "injected";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("registrar.RegisterTransient<Service>();")]
    [TestCase("Registrar.RegisterTransient<Service>(registrar);")]
    [TestCase("registrar?.RegisterTransient<Service>();")]
    [TestCase(RegistrationMethodGroup)]
    [TestCase("System.Action<object, System.Action<Service>> register = registrar.RegisterTransient<Service>; register(null, null);")]
    [TestCase("registrar.RegisterTransient(cleanup: (Service service) => { });")]
    public void RegistrationSyntax_ShouldDiscoverClosedTypeAndBindGeneratedOverload(string registration)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;
            using Registrar = Macaron.DependencyInjection.DependencyRegistrarExtensions;

            public sealed class Service
            {
                public string Value { get; }

                public Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: "injected");
                        {{registration}}
                    });

                    return container.Resolve<Service>().Value == "injected";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void PrivateNestedTarget_ShouldReportInaccessibleImplementation()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                private class Service { }

                public static void Install(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service>();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0004"]));
        AssertCompiles(compilation);
    }

    [Test]
    public void InferredInvalidTarget_ShouldReportAtMethodName()
    {
        const string source =
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                private Service() { }
            }

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient(cleanup: (Service service) => { });
                }
            }
            """;
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();
        var span = diagnostic.Location.SourceSpan;
        var locationText = source.Substring(span.Start, span.Length);

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0006"));
        Assert.That(locationText, Is.EqualTo("RegisterTransient"));
        AssertCompiles(compilation);
    }

    [Test]
    public void SingleAndTwoTypeRegistrations_ShouldShareFactoryAndKeepSeparateRegistrations()
    {
        var (compilation, sources) = Generate(
            """
            using Macaron.DependencyInjection;

            public interface IService
            {
                string Value { get; }
            }

            public sealed class Service : IService
            {
                public string Value { get; }

                public Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: "shared");
                        registrar.RegisterSingleton<Service>();
                        registrar.RegisterScoped<IService, Service>();
                    });

                    return container.Resolve<Service>().Value == "shared"
                        && container.Resolve<IService>().Value == "shared"
                        && !object.ReferenceEquals(container.Resolve<Service>(), container.Resolve<IService>());
                }
            }
            """
        );
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories.Split("factories.Add(").Length - 1, Is.EqualTo(1));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void FactoryAndInstanceOverloads_ShouldRemainRuntimeMethodsAndSupportGenericHelpers()
    {
        var (compilation, sources) = Generate(
            """
            using Macaron.DependencyInjection;

            public interface IService { }

            public sealed class Service : IService
            {
                public Service(int value) { }
            }

            public static class Probe
            {
                private static void Install<T>(IDependencyRegistrar registrar, System.Func<T> factory) where T : notnull
                {
                    registrar.Register<T>(factory, Lifetime.Transient);
                }

                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        Install<IService>(registrar, () => new Service(1));
                        registrar.RegisterInstance(instance: new Service(2), tag: "instance");
                        registrar.RegisterScoped<Service>(
                            (IDependencyResolver resolver, ref CleanupCollector collector) => new Service(3),
                            tag: "scoped"
                        );
                        registrar.RegisterTransient<Service>(() => new Service(4));
                    });

                    return container.Resolve<IService>() is Service && container.Resolve<Service>() is Service;
                }
            }
            """
        );
        var input = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Input.cs");
        var semanticModel = compilation.GetSemanticModel(input);
        var registrations = input.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(invocation => semanticModel.GetSymbolInfo(invocation).Symbol)
            .OfType<IMethodSymbol>()
            .Where(method => method.Name.StartsWith("Register"));
        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(registrations.All(method => method.ContainingType.Name != "DependencyRegistrarExtensions"), Is.True);
        Assert.That(factories, Does.Not.Contain("factories.Add("));
        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void EquivalentSingleAndTwoTypeRegistration_ShouldReuseFactoryRendering()
    {
        const string source =
            """
            using Macaron.DependencyInjection;

            public class Service { }

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service>();
                }
            }
            """;
        var driver = CreateDriver(trackIncrementalSteps: true).RunGeneratorsAndAnalyze(
            CreateCompilation(source),
            out _,
            out var diagnostics
        );

        Assert.That(diagnostics, Is.Empty);

        driver = driver.RunGeneratorsAndAnalyze(
            CreateCompilation(source.Replace("RegisterTransient<Service>", "RegisterTransient<Service, Service>")),
            out var output,
            out diagnostics
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            driver.GetRunResult().Results.Single().TrackedSteps["FactoriesSource"].Single().Outputs.Single().Reason,
            Is.EqualTo(IncrementalStepRunReason.Cached)
        );
        AssertCompiles(output);
    }

    [TestCase("Transient")]
    [TestCase("Scoped")]
    [TestCase("Singleton")]
    public void MissingDependency_ShouldFailThroughNormalBuildOrResolve(string lifetime)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public interface IDependency { }

            public sealed class Service
            {
                public Service(IDependency dependency) { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(registrar =>
                        {
                            registrar.Register<Service>(Lifetime.{{lifetime}});
                        });
                        container.Resolve<Service>();
                        return false;
                    }
                    catch (System.Exception exception)
                    {
                        while (exception.InnerException != null) { exception = exception.InnerException; }
                        return exception is System.InvalidOperationException && exception.Message.Contains("IDependency");
                    }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }
}
