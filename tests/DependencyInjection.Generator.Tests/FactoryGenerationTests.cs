using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class FactoryGenerationTests
{
    [TestCase("Register", Lifetime.Scoped, false)]
    [TestCase("RegisterTransient", Lifetime.Transient, false)]
    [TestCase("RegisterScoped", Lifetime.Scoped, false)]
    [TestCase("RegisterSingleton", Lifetime.Singleton, false)]
    [TestCase("Register", Lifetime.Scoped, true)]
    [TestCase("RegisterTransient", Lifetime.Transient, true)]
    [TestCase("RegisterScoped", Lifetime.Scoped, true)]
    [TestCase("RegisterSingleton", Lifetime.Singleton, true)]
    public void Registration_ShouldInjectDefaultDependencyAndPreserveLifetimeAndCleanup(
        string registrationMethod,
        Lifetime lifetime,
        bool singleType
    )
    {
        var requiredArguments = registrationMethod == "Register" ? $"Lifetime.{lifetime}," : "";
        var expectedRootReuse = lifetime != Lifetime.Transient ? "true" : "false";
        var expectedChildReuse = lifetime == Lifetime.Singleton ? "true" : "false";
        var expectedCleanups = lifetime == Lifetime.Transient ? 4 : lifetime == Lifetime.Scoped ? 2 : 1;
        var typeArguments = singleType ? "Service" : "IService, Service";
        var serviceType = singleType ? "Service" : "IService";
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
                        registrar.{{registrationMethod}}<{{typeArguments}}>({{requiredArguments}} tag: "service", cleanup: _ => cleanups++);
                    });
                    using var child = container.CreateScope();
                    using var transientScope = container.CreateTransientScope();
                    var first = container.Resolve<{{serviceType}}>("service");
                    var second = container.Resolve<{{serviceType}}>("service");
                    var inherited = child.Resolve<{{serviceType}}>("service");
                    var scoped = transientScope.Resolve<{{serviceType}}>("service");
                    var injected = object.ReferenceEquals(first.Dependency, container.Resolve<IDependency>());
                    var rootReuse = object.ReferenceEquals(first, second) == {{expectedRootReuse}};
                    var childReuse = object.ReferenceEquals(first, inherited) == {{expectedChildReuse}};
                    var transientScopeReuse = object.ReferenceEquals(first, scoped) == {{expectedRootReuse}};

                    transientScope.Dispose();
                    child.Dispose();
                    container.Dispose();

                    return injected && rootReuse && childReuse && transientScopeReuse && cleanups == {{expectedCleanups}};
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

    [TestCase("class", false)]
    [TestCase("struct", false)]
    [TestCase("class", true)]
    [TestCase("struct", true)]
    public void ImplicitDefaultConstructor_ShouldCreateService(string typeKind, bool singleType)
    {
        var typeArguments = singleType ? "Service" : "IService, Service";
        var serviceType = singleType ? "Service" : "IService";
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public interface IService { }

            public {{typeKind}} Service : IService { }

            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<{{typeArguments}}>());

                    return container.Resolve<{{serviceType}}>() is Service;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosedNestedGenericType_ShouldUseSubstitutedConstructorParameters(bool singleType)
    {
        var typeArguments = singleType ? "Outer<string>.Inner<int>" : "object, Outer<string>.Inner<int>";
        var serviceType = singleType ? "Outer<string>.Inner<int>" : "object";
        var (compilation, _) = Generate(
            $$"""
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
                        registrar.RegisterTransient<{{typeArguments}}>();
                    });
                    var service = (Outer<string>.Inner<int>)container.Resolve<{{serviceType}}>();

                    return service.First == "nested" && service.Second == 42;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
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

            public static class Other
            {
                public static void Register<T>() { }
                public static void Register<TService, TImplementation>() { }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    Other.Register<Service, Service>();
                    Other.Register<Service>();

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

    [TestCase("public", "external", false)]
    [TestCase("public", "external", true)]
    public void ReferencedImplementation_ShouldSelectAccessibleConstructor(
        string accessibility,
        string expectedValue,
        bool singleType
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
        var typeArguments = singleType ? "Service" : "Service, Service";
        var (compilation, _, diagnostics) = GenerateWithDiagnostics(
            $$"""
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<string>(() => "external");
                        registrar.RegisterTransient<{{typeArguments}}>();
                    });

                    return container.Resolve<Service>().Value;
                }
            }
            """,
            assemblyName: "ExternalConsumer",
            additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(diagnostics, Is.Empty);
        AssertCompiles(compilation);

        Assert.That(Execute<string>(compilation, external), Is.EqualTo(expectedValue));
    }
}
