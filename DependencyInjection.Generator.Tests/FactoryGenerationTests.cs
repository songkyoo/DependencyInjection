using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryGenerationTests
{
    [TestCase("Register", Lifetime.Scoped)]
    [TestCase("RegisterTransient", Lifetime.Transient)]
    [TestCase("RegisterScoped", Lifetime.Scoped)]
    [TestCase("RegisterSingleton", Lifetime.Singleton)]
    public void Registration_ShouldInjectDefaultDependencyAndPreserveLifetimeAndCleanup(
        string registrationMethod,
        Lifetime lifetime
    )
    {
        var requiredArguments = registrationMethod == "Register" ? $"Lifetime.{lifetime}," : "";
        var expectedRootReuse = lifetime != Lifetime.Transient ? "true" : "false";
        var expectedChildReuse = lifetime == Lifetime.Singleton ? "true" : "false";
        var expectedCleanups = lifetime == Lifetime.Transient ? 3 : lifetime == Lifetime.Scoped ? 2 : 1;
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public interface IDependency { }

            public sealed class Dependency : IDependency { }

            public interface IService { IDependency Dependency { get; } }

            public sealed class Service : IService
            {
                public IDependency Dependency { get; }

                public Service(IDependency dependency) { Dependency = dependency; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var cleanups = 0;
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IDependency>(() => new Dependency());
                        registrar.RegisterSingleton<IDependency>(() => new Dependency(), tag: "other");
                        registrar.{{registrationMethod}}<IService, Service>({{requiredArguments}} tag: "service", cleanup: _ => cleanups++);
                    });
                    using var child = container.CreateScope();
                    var first = container.Resolve<IService>("service");
                    var second = container.Resolve<IService>("service");
                    var inherited = child.Resolve<IService>("service");
                    var injected = object.ReferenceEquals(first.Dependency, container.Resolve<IDependency>());
                    var rootReuse = object.ReferenceEquals(first, second) == {{expectedRootReuse}};
                    var childReuse = object.ReferenceEquals(first, inherited) == {{expectedChildReuse}};

                    child.Dispose();
                    container.Dispose();

                    return injected && rootReuse && childReuse && cleanups == {{expectedCleanups}};
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("public")]
    [TestCase("internal")]
    public void InjectConstructor_ShouldOverrideOtherPublicConstructors(string accessibility)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;
            public sealed class Service
            {
                public string Value { get; }

                public Service() { Value = "default"; }

                [Inject] {{accessibility}} Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<string>(() => "injected");
                        registrar.RegisterTransient<Service, Service>();
                    });
                    return container.Resolve<Service>().Value == "injected";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void SinglePublicConstructor_ShouldIgnorePrivateConstructors()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string Value { get; }

                private Service() { Value = "private"; }

                public Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<string>(() => "public");
                        registrar.RegisterTransient<Service, Service>();
                    });
                    return container.Resolve<Service>().Value == "public";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("class")]
    [TestCase("struct")]
    public void ImplicitDefaultConstructor_ShouldCreateService(string typeKind)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public interface IService { }

            public {{typeKind}} Service : IService { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<IService, Service>());

                    return container.Resolve<IService>() is Service;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ClosedNestedGenericType_ShouldUseSubstitutedConstructorParameters()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Outer<T>
            {
                public class Inner<TValue>
                {
                    public T First { get; }

                    public TValue Second { get; }

                    public Inner() { }

                    [Inject] public Inner(T first, TValue second) { First = first; Second = second; }
                }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<string>(() => "nested");
                        registrar.RegisterSingleton<int>(() => 42);
                        registrar.RegisterTransient<object, Outer<string>.Inner<int>>();
                    });
                    var service = (Outer<string>.Inner<int>)container.Resolve<object>();

                    return service.First == "nested" && service.Second == 42;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("public Service() { } public Service(string value) { }", "MADI0007")]
    [TestCase("[Inject] public Service() { } [Inject] public Service(string value) { }", "MADI0005")]
    [TestCase("private Service() { }", "MADI0006")]
    [TestCase("public Service() { } [Inject] private Service(string value) { }", "MADI0008")]
    [TestCase("public Service(ref int value) { }", "MADI0009")]
    [TestCase("public Service(in int value) { }", "MADI0009")]
    [TestCase("public Service(out int value) { value = 0; }", "MADI0009")]
    [TestCase("public Service(System.Span<int> value) { }", "MADI0009")]
    [TestCase("public Service(dynamic value) { }", "MADI0002")]
    [TestCase("public Service(dynamic[] value) { }", "MADI0002")]
    [TestCase("public Service(System.Collections.Generic.List<dynamic> value) { }", "MADI0002")]
    public void UnsupportedConstructor_ShouldReportDiagnostic(string constructors, string expectedId)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service { {{constructors}} }

            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Service, Service>());

                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo([expectedId]));
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

                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(registrar => Configure<string>(registrar));

                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
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
        var (compilation, sources, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service<T> { }

            public class Outer<T> { public sealed class Inner { } }

            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(
                            registrar => registrar.RegisterTransient<object, {{implementation}}>());

                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
                }
            }
            """
        );

        var factories = sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString();

        Assert.That(factories, Does.Not.Contain("factories.Add("));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0002"]));
        AssertCompiles(compilation);
    }

    [Test]
    public void RepeatedImplementation_ShouldHaveOneFactoryAndIndependentRegistrations()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public interface IFirst { }

            public interface ISecond { }

            public sealed class Service : IFirst, ISecond
            {
                public static int Created;

                public Service() { Created++; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IFirst, Service>();
                        DependencyRegistrarExtensions.RegisterScoped<ISecond, Service>(registrar);
                        registrar.RegisterTransient<Service, Service>();
                    });

                    return container.Resolve<IFirst>() is Service
                        && container.Resolve<ISecond>() is Service
                        && container.Resolve<Service>() is Service
                        && Service.Created == 3;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("string", "string?")]
    [TestCase("(int Number, string Text)", "(int Count, string Name)")]
    [TestCase("(int Number, string Text)", "System.ValueTuple<int, string>")]
    [TestCase("(int Number, string Text)[]", "System.ValueTuple<int, string>[]")]
    [TestCase("System.Collections.Generic.List<(int Number, string Text)>", "System.Collections.Generic.List<(int Count, string Name)>")]
    public void EquivalentRuntimeTypes_ShouldShareFactoryDespiteSourceAnnotations(
        string firstArgument,
        string secondArgument)
    {
        var (compilation, _) = Generate(
            $$"""
            #nullable enable

            using Macaron.DependencyInjection;

            public interface IFirst { }

            public interface ISecond { }

            public sealed class Service<T> : IFirst, ISecond { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IFirst, Service<{{firstArgument}}>>();
                        registrar.RegisterScoped<ISecond, Service<{{secondArgument}}>>();
                    });

                    return container.Resolve<IFirst>() != null && container.Resolve<ISecond>() != null;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void DistinctClosedImplementations_ShouldHaveSeparateFactories()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public interface IFirst { }

            public interface ISecond { }

            public sealed class Service<T> : IFirst, ISecond { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<IFirst, Service<string>>();
                        registrar.RegisterSingleton<ISecond, Service<object>>();
                    });

                    return container.Resolve<IFirst>() is Service<string>
                        && container.Resolve<ISecond>() is Service<object>;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void UnrelatedRegistrationMethod_ShouldNotProvideFactory()
    {
        var (compilation, _) = Generate(
            """
            public sealed class Service { }

            public static class Other { public static void Register<TService, TImplementation>() { } }

            public static class Probe
            {
                public static bool Run()
                {
                    Other.Register<Service, Service>();

                    try
                    {
                        Macaron.DependencyInjection.Generated.Factories.Get<Service>();

                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase("DependencyRegistrarExtensions.RegisterTransient<Service, Service>(registrar);")]
    [TestCase("Registrar.RegisterTransient<Service, Service>(registrar);")]
    [TestCase("registrar?.RegisterTransient<Service, Service>();")]
    [TestCase("System.Action<IDependencyRegistrar, object, System.Action<Service>> register = Registrar.RegisterTransient<Service, Service>; register(registrar, null, null);")]
    public void RegistrationSyntax_ShouldIdentifyGeneratedMethod(string registration)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            using Registrar = Macaron.DependencyInjection.DependencyRegistrarExtensions;

            public sealed class Service { }

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
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ArrayParameter_ShouldResolveArrayAsOneDependency()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string[] Values { get; }

                public Service(params string[] values) { Values = values; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<string[]>(() => new[] { "array" });
                        registrar.RegisterTransient<Service, Service>();
                    });

                    return container.Resolve<Service>().Values[0] == "array";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void MixedArrayRanks_ShouldPreserveParameterType()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string[][,] Values { get; }

                public Service(string[][,] values) { Values = values; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var values = new string[1][,];

                    values[0] = new[,] { { "array" } };

                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<string[][,]>(() => values);
                        registrar.RegisterTransient<Service, Service>();
                    });

                    return container.Resolve<Service>().Values[0][0, 0] == "array";
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void QualifiedTypeNames_ShouldHandleKeywordsAndTupleParameters()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            namespace @class
            {
                public sealed class @event
                {
                    public (int Number, string Text) Value { get; }

                    public object Dependency { get; }

                    public @event((int Number, string Text) value, object dependency)
                    {
                        Value = value;
                        Dependency = dependency;
                    }
                }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<(int, string)>(() => (42, "tuple"));
                        registrar.RegisterSingleton<object>(() => new object());
                        registrar.RegisterTransient<@class.@event, @class.@event>();
                    });

                    var service = container.Resolve<@class.@event>();

                    return service.Value.Number == 42
                        && service.Value.Text == "tuple"
                        && object.ReferenceEquals(service.Dependency, container.Resolve<object>());
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ImplicitStructConstructor_ShouldParticipateInPublicConstructorAmbiguity()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public struct Service { public Service(int value) { } }

            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Service, Service>());

                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0007"]));
        AssertCompiles(compilation);
    }

    [TestCase("public abstract class Service { }")]
    [TestCase("public interface Service { }")]
    public void NonConcreteImplementation_ShouldReportDiagnostic(string declaration)
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            {{declaration}}

            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<object, Service>());

                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0003"]));
        AssertCompiles(compilation);
    }

    [Test]
    public void PrivateNestedImplementation_ShouldReportDiagnostic()
    {
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                private sealed class Service { }

                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Service, Service>());

                        return false;
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return true;
                    }
                }
            }
            """
        );

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(["MADI0004"]));
        AssertCompiles(compilation);
    }

    [TestCase("public", true)]
    [TestCase("internal", false)]
    public void ReferencedImplementation_ShouldHonorInjectMetadataAndAccessibility(
        string accessibility,
        bool expectedFactory
    )
    {
        var external = EmitAssembly(CreateCompilation(
            $$"""
            using Macaron.DependencyInjection;

            public sealed class Service
            {
                public string Value { get; }

                public Service() { Value = "default"; }

                [Inject] {{accessibility}} Service(string value) { Value = value; }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    try
                    {
                        using var container = DependencyContainerFactory.Create(registrar =>
                        {
                            registrar.RegisterSingleton<string>(() => "external");
                            registrar.RegisterTransient<Service, Service>();
                        });

                        return container.Resolve<Service>().Value == "external";
                    }
                    catch (System.Collections.Generic.KeyNotFoundException)
                    {
                        return false;
                    }
                }
            }
            """,
            assemblyName: "ExternalConsumer",
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(expectedFactory ? Array.Empty<string>() : ["MADI0008"])
        );
        AssertCompiles(compilation);

        if (expectedFactory)
        {
            Assert.That(Execute<bool>(compilation, external), Is.True);
        }
    }

    [Test]
    public void ConstructorChange_ShouldUpdateFactoryWhenRegistrationSyntaxIsUnchanged()
    {
        var input = CreateCompilation(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<string>(() => "updated");
                        registrar.RegisterTransient<Service, Service>();
                    });

                    return container.Resolve<Service>().Value;
                }
            }
            """
        );

        var originalType = CSharpSyntaxTree.ParseText(
            text: "public sealed class Service { public string Value => \"original\"; }",
            options: InputParseOptions,
            path: "Service.cs"
        );

        input = input.AddSyntaxTrees(originalType);

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(first), Is.EqualTo("original"));

        var updatedType = CSharpSyntaxTree.ParseText(
            text: "public sealed class Service { public string Value { get; } public Service(string value) { Value = value; } }",
            options: InputParseOptions,
            path: "Service.cs"
        );
        var updatedInput = input.ReplaceSyntaxTree(originalType, updatedType);

        driver.RunGeneratorsAndUpdateCompilation(updatedInput, out var updated, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<string>(updated), Is.EqualTo("updated"));
    }

    [Test]
    public void RemovingLastRegistration_ShouldRemoveFactoryFromCollectedOutput()
    {
        const string source =
            """
            using Macaron.DependencyInjection;

            public sealed class Service { }

            public static class Probe
            {
                public static void Configure(IDependencyRegistrar registrar)
                {
                    registrar.RegisterTransient<Service, Service>();
                }

                public static bool Run()
                {
                    try { return Macaron.DependencyInjection.Generated.Factories.Get<Service>() != null; }
                    catch (System.Collections.Generic.KeyNotFoundException) { return false; }
                }
            }
            """;
        var input = CreateCompilation(source);
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var first, out var diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(first), Is.True);

        var updatedSource = source.Replace("registrar.RegisterTransient<Service, Service>();", "");
        var updatedInput = CreateCompilation(updatedSource);

        driver.RunGeneratorsAndUpdateCompilation(updatedInput, out var updated, out diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(Execute<bool>(updated), Is.False);
    }
}
