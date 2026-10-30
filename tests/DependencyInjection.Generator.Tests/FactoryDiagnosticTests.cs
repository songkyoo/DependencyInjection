using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryDiagnosticTests
{
    [TestCase("Register", "Lifetime.Transient")]
    [TestCase("RegisterTransient", "")]
    [TestCase("RegisterScoped", "")]
    [TestCase("RegisterSingleton", "")]
    public void EveryRegistrationMethod_ShouldReportConstructorFailure(string method, string arguments)
    {
        var (_, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;
            public class Service { private Service() { } }
            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.{{method}}<object, Service>({{arguments}});
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0006"]));
    }

    [Test]
    public void DuplicateInjectConstructors_ShouldIncludeAttributeLocations()
    {
        const string source =
            """
            using Macaron.DependencyInjection;
            public class Service
            {
                [Inject] public Service() { }
                [Inject] public Service(int value) { }
            }
            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<object, Service>();
                }
            }
            """;
        var (_, _, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0005"));
        Assert.That(
            new[] { diagnostic.Location }
                .Concat(diagnostic.AdditionalLocations)
                .Select(location => source.Substring(location.SourceSpan.Start, location.SourceSpan.Length)),
            Is.EqualTo(["Inject", "Inject"])
        );
    }

    [Test]
    public void DynamicImplementation_ShouldBeDiagnosedEvenWhenEquivalentObjectFactoryExists()
    {
        var (_, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service<T> { }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterSingleton<object, Service<object>>();
                    registrar.RegisterTransient<object, Service<dynamic>>();
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0002"]));
    }

    [Test]
    public void ExternalConstructorWithInaccessibleDependency_ShouldReportDependencyFailureAtUse()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("ExternalServices"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("ExternalServices");
        var dependency = module.DefineType("Dependency", TypeAttributes.NotPublic).CreateType();
        var service = module.DefineType("Service", TypeAttributes.Public);
        var constructor = service.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [dependency]);
        var body = constructor.GetILGenerator();

        body.Emit(OpCodes.Ldarg_0);
        body.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        body.Emit(OpCodes.Ret);
        service.CreateType();

        using var image = new MemoryStream();

        builder.Save(image);

        const string source =
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterTransient<object, Service>();
                    resolver.For<Service>();
                }
            }
            """;

        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            source,
            additionalReferences: MetadataReference.CreateFromImage(image.ToArray())
        );
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "MADI0009", "MADI0009" }));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan.Start), Is.EqualTo(new[]
        {
            source.IndexOf("Service>", StringComparison.Ordinal),
            source.LastIndexOf("Service>", StringComparison.Ordinal),
        }));
        Assert.That(diagnostics.SelectMany(diagnostic => diagnostic.AdditionalLocations), Is.Empty);
        Assert.That(diagnostics.Select(diagnostic => diagnostic.GetMessage()), Is.All.Contains("Dependency"));
        AssertCompiles(compilation);
    }

    [TestCase("int*")]
    [TestCase("delegate*<void>")]
    public void UnsafeConstructorParameter_ShouldReportUnsupportedParameterWithSourceLocation(string parameterType)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public unsafe class Service { public Service({{parameterType}} value) { } }

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<object, Service>();
                }
            }
            """;
        var input = CreateCompilation(source);

        input = input.WithOptions(input.Options.WithAllowUnsafe(true));

        CreateDriver().RunGeneratorsAndAnalyze(input, out var output, out var diagnostics);

        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("MADI0008"));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.LastIndexOf("Service", StringComparison.Ordinal)));
        Assert.That(
            diagnostic.AdditionalLocations.Select(location => source.Substring(location.SourceSpan.Start, location.SourceSpan.Length)),
            Is.EqualTo(["value"])
        );
        AssertCompiles(output);
    }

    [Test]
    public void OrdinaryDynamicRegistrationAndResolution_ShouldRemainOutsideFactoryDiagnostics()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyRegistrar registrar, IDependencyResolver resolver)
                {
                    registrar.RegisterSingleton<dynamic>(() => new object());
                    resolver.Resolve<dynamic>();
                }
            }
            """
        );

        AssertCompiles(compilation);
    }

    [TestCase("Service<T>")]
    [TestCase("Service<T[]>")]
    [TestCase("Outer<T>.Inner")]
    public void OpenImplementation_ShouldNotBeInferredFromGenericHelperCalls(string implementation)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public interface IService { }

            public sealed class Service<T> : IService { }

            public class Outer<T> { public sealed class Inner : IService { } }

            public static class Probe
            {
                private static void Configure<T>(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<IService, {{implementation}}>();
                }

                public static void ConfigureClosed(IDependencyRegistrar registrar)
                {
                    Configure<string>(registrar);
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0001"]));
        AssertCompiles(compilation);
    }

    [TestCase("dynamic")]
    [TestCase("Service<dynamic>")]
    [TestCase("Service<dynamic[]>")]
    [TestCase("Service<System.Collections.Generic.List<dynamic>>")]
    [TestCase("Outer<dynamic>.Inner")]
    public void DynamicImplementation_ShouldReportDiagnostic(string implementation)
    {
        var source =
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service<T> { }

            public class Outer<T> { public sealed class Inner { } }

            public static class Probe
            {
                public static void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<object, {{implementation}}>();
                }
            }
            """;
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(source);
        var diagnostic = diagnostics.Single();

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.Location.SourceSpan.Start, Is.EqualTo(source.LastIndexOf(implementation, StringComparison.Ordinal)));
        Assert.That(diagnostic.Location.SourceSpan.Length, Is.EqualTo(implementation.Length));
        Assert.That(diagnostic.GetMessage(), Does.Contain("dynamic"));

        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("factories.Add("));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0002"]));
        AssertCompiles(compilation);
    }
}
