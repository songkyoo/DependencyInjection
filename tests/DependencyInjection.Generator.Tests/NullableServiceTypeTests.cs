using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class NullableServiceTypeTests
{
    [TestCase("registrar.RegisterTransient<IService?, Service>();")]
    [TestCase("registrar.RegisterScoped<IService?, Service>();")]
    [TestCase("registrar.RegisterSingleton<IService?, Service>();")]
    [TestCase("registrar.Register<IService?, Service>(Lifetime.Singleton);")]
    public void NullableReferenceService_ShouldUseGeneratedFactoryAndNonNullableIdentity(string registration)
    {
        var (compilation, _) = Generate(
            $$"""
            #nullable enable
            using Macaron.DependencyInjection;

            public interface IService
            {
                string Value { get; }
            }

            public sealed class Service : IService
            {
                public string Value { get; }

                public Service(string? value) { Value = value!; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: "value");
                        {{registration}}
                    });

                    return container.Contains<IService>()
                        && container.Resolve<IService?>()!.Value == "value"
                        && container.For<Service>().Create().Value == "value";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("registrar.RegisterTransient<Value?, Value>();")]
    [TestCase("registrar.RegisterScoped<Value?, Value>();")]
    [TestCase("registrar.RegisterSingleton<Value?, Value>();")]
    [TestCase("registrar.Register<Value?, Value>(Lifetime.Transient);")]
    public void NullableValueServiceWithUnderlyingImplementation_ShouldKeepCompilerConstraintError(string registration)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public struct Value { }

            public static class Configuration
            {
                public static void Install(IDependencyRegistrar registrar)
                {
                    {{registration}}
                }
            }
            """
        );

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id);

        Assert.That(errors, Is.EqualTo(new[] { "CS0315" }));
    }
}
