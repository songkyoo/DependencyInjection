using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class RegistrationSourceApiTests
{
    private const string ContextFactoryDeclaration =
        """
        DependencyFactory<object> factory =
            (IDependencyResolver resolver, ref CleanupCollector collector) => new object();
        """;

    [TestCase("Func<object> factory = () => new object();")]
    [TestCase(ContextFactoryDeclaration)]
    public void SingletonFactoryAndDelegateInstance_ShouldCompileWithoutNamedArguments(string factoryDeclaration)
    {
        var compilation = CreateCompilation(
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    {{factoryDeclaration}}

                    registrar.RegisterSingleton(factory, cleanup: _ => { });
                    registrar.RegisterInstance(factory, cleanup: _ => { });
                }
            }
            """
        );

        AssertCompiles(compilation);
    }

    [TestCase("RegisterTransient", "Factory<object>(() => new object())")]
    [TestCase("RegisterTransient", "ContextFactory<object>(factory)")]
    [TestCase("RegisterScoped", "Factory<object>(() => new object())")]
    [TestCase("RegisterScoped", "ContextFactory<object>(factory)")]
    [TestCase("RegisterSingleton", "Instance<object>(new object())")]
    [TestCase("RegisterSingleton", "Factory<object>(() => new object())")]
    [TestCase("RegisterSingleton", "ContextFactory<object>(factory)")]
    public void AllowedSourceCase_ShouldCompileWithInferredServiceType(string method, string source)
    {
        var compilation = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    DependencyFactory<object> factory =
                        (IDependencyResolver resolver, ref CleanupCollector collector) => new object();

                    registrar.{{method}}(source: new RegistrationSource.{{source}});
                }
            }
            """
        );

        AssertCompiles(compilation);
    }

    [TestCase("RegisterTransient")]
    [TestCase("RegisterScoped")]
    public void InstanceSource_ShouldNotCompileForNonSingletonLifetime(string method)
    {
        var compilation = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    registrar.{{method}}<object>(source: new RegistrationSource.Instance<object>(new object()));
                }
            }
            """
        );
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Id, Is.EqualTo("CS1503"));
    }

    [TestCase("RegisterTransient")]
    [TestCase("RegisterScoped")]
    [TestCase("RegisterSingleton")]
    public void BaseSource_ShouldNotCompileAtTheRegistrationBoundary(string method)
    {
        var compilation = CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    RegistrationSource source = new RegistrationSource.Factory<object>(() => new object());

                    registrar.{{method}}<object>(source: source);
                }
            }
            """
        );
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Id, Is.EqualTo("CS1503"));
    }

    [Test]
    public void Delegate_ShouldNotImplicitlyConvertToAFactorySource()
    {
        var compilation = CreateCompilation(
            """
            using System;
            using Macaron.DependencyInjection;

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    Func<object> factory = () => new object();

                    registrar.RegisterTransient<object>(source: factory);
                }
            }
            """
        );
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Id, Is.EqualTo("CS1503"));
    }
}
