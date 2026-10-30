using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class AccessibleInjectionSelectionTests
{
    [TestCase("public")]
    [TestCase("internal")]
    [TestCase("protected internal")]
    public void SingleAccessibleConstructor_ShouldSupportRegistrationAndCreateWithoutInject(string accessibility)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public int Value;

                private Service() { Value = -1; }

                {{accessibility}} Service(int value) { Value = value; }
            }
            public static class Probe
            {
                public static (int, int) Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: 42);
                        registrar.RegisterTransient<Service>();
                    });

                    return (container.Resolve<Service>().Value, container.For<Service>().Create().Value);
                }
            }
            """
        );

        Assert.That(Execute<(int, int)>(compilation), Is.EqualTo((42, 42)));
    }

    [Test]
    public void UnmarkedInaccessibleMembers_ShouldNotAffectSelectionOrParameterDiagnostics()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public int Value;

                public Service(int value) { Value = value; }

                private Service([Assisted] ref int value) { }

                private static int Initialize([Tag(new[] { 1 }), Assisted] int value) => value;
            }
            public static class Probe
            {
                public static (int, int, int) Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: 42);
                        registrar.RegisterTransient<Service>();
                        registrar.RegisterSingleton(instance: new Service(9), tag: "explicit");
                    });

                    return (container.Resolve<Service>().Value, container.For<Service>().Create().Value,
                        container.Resolve<Service>(tag: "explicit").Value);
                }
            }
            """
        );

        Assert.That(Execute<(int, int, int)>(compilation), Is.EqualTo((42, 42, 9)));
    }

    [Test]
    public void InternalInjectMethod_ShouldRemainSelectedWithUnmarkedPrivateConstructor()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public int Value;

                public Service() { }

                private Service(int value) { }

                [Inject] internal void Initialize([Assisted] int value) { Value = value; }
            }
            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var service = new Service();

                    container.For<Service>().Inject(service, 42);

                    return service.Value;
                }
            }
            """
        );

        Assert.That(Execute<int>(compilation), Is.EqualTo(42));
    }

    [Test]
    public void InaccessibleInjectConstructor_ShouldPreventFallbackToUnmarkedConstructors()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public Service() { }

                internal Service(int value) { }

                [Inject] private Service(string value) { }
            }
            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0018"]));
        Assert.That(diagnostics.Single().GetMessage(), Does.Contain("marked with Inject is inaccessible"));
        AssertCompiles(compilation);
    }

    [Test]
    public void InternalInjectConstructor_ShouldResolveAmbiguousAccessibleConstructors()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public int Value;

                public Service() { Value = -1; }

                [Inject] internal Service(int value) { Value = value; }

                private void Initialize() { }
            }
            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(instance: 42));

                    return container.For<Service>().Create().Value;
                }
            }
            """
        );

        Assert.That(Execute<int>(compilation), Is.EqualTo(42));
    }

    [Test]
    public void ExternalUnmarkedPrivateMembers_ShouldNotBlockDefaultConstructorOrRegistration()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public int Value;

                public Service() { Value = 42; }

                private Service([Assisted] int value) { }

                private void Initialize([Tag(new[] { 1 }), Assisted] int value) { }
            }
            """, assemblyName: "ExternalServices"
        ));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static (int, int) Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Service>());

                    return (container.Resolve<Service>().Value, container.For<Service>().Create().Value);
                }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<(int, int)>(compilation, external), Is.EqualTo((42, 42)));
    }

    [Test]
    public void ExternalUnmarkedInternalConstructor_WithFriendAccess_ShouldSupportAutomaticSelection()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]
            public class Service
            {
                public int Value;

                internal Service(int value) { Value = value; }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static (int, int) Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton(instance: 42);
                        registrar.RegisterTransient<Service>();
                    });

                    return (container.Resolve<Service>().Value, container.For<Service>().Create().Value);
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<(int, int)>(compilation, external), Is.EqualTo((42, 42)));
    }

    [Test]
    public void ExternalUnmarkedInternalConstructor_WithoutFriendAccess_ShouldReportNoAccessibleConstructor()
    {
        var external = EmitAssembly(CreateCompilation(
            "public class Service { internal Service(int value) { } }",
            assemblyName: "ExternalServices"
        ));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0006"]));
        Assert.That(diagnostics.Single().GetMessage(), Does.Contain("no constructor accessible"));
        AssertCompiles(compilation);
    }

    [Test]
    public void ExternalProtectedTarget_UsedInsideDerivedClass_ShouldStillBeRejectedForGeneratedCode()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public class Base
            {
                protected class Service { public void Initialize() { } }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;
            public class Derived : Base
            {
                public void Configure(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """,
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0004"]));
        AssertCompiles(compilation);
    }

    [TestCase("private Service() { }", "MADI0006")]
    [TestCase("public Service() { } internal Service(int value) { }", "MADI0007")]
    public void ConstructorSelectionFailure_ShouldNotAddEntryPointMismatch(string constructors, string expectedId)
    {
        var (_, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public class Service { {{constructors}} }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver, Service value) { resolver.For<Service>().Inject(value); }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]));
    }
}
