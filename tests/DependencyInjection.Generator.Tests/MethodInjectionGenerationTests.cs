using Microsoft.CodeAnalysis;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class MethodInjectionGenerationTests
{
    [Test]
    public void ClosedGenericMethod_ShouldPreserveMixedArgumentsAndAvoidGeneratedNameCollisions()
    {
        var (compilation, sources) = Generate(
            """
            #nullable enable

            using Macaron.DependencyInjection;

            public class Service<T>
            {
                public static int Constructors;

                public string Value = "";

                private Service() { Constructors++; }

                public static Service<T> Existing() => new Service<T>();

                [Inject]
                public void @event(
                    [Tag("primary")] in T primary,
                    [Assisted] string instance,
                    [Assisted] in int context,
                    int? optional = 7,
                    [Assisted] string resolver = "provided",
                    [Assisted] params string[] names)
                {
                    Value = $"{primary}|{instance}|{context}|{optional}|{resolver}|{string.Join(",", names)}";
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: "chosen", tag: "primary");
                        registrar.RegisterInstance(instance: 42);
                    });
                    var service = Service<string>.Existing();
                    var value = 3;

                    container.For<Service<string>>().Inject(service, "caller", in value, names: new[] { "a", "b" });

                    return $"{service.Value}|{Service<string>.Constructors}";
                }
            }
            """
        );
        var generated = sources
            .Single(source => source.HintName.StartsWith("InjectionContextExtensions."))
            .SourceText
            .ToString();

        Assert.That(Execute<string>(compilation), Is.EqualTo("chosen|caller|3|42|provided|a,b|1"));
        Assert.That(generated, Does.Contain("void Inject(").And.Contain(".@event(").And.Not.Contain(" Create("));
        Assert.That(
            sources.Single(source => source.HintName.StartsWith("Factories.")).SourceText.ToString(),
            Does.Not.Contain("typeof(global::Service")
        );
    }

    [Test]
    public void StructTarget_ShouldMutateOriginalStorageWithoutConstructingOrCopyingIt()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public struct State
            {
                public int Value;

                [Inject] public void Initialize(in int value, [Assisted] int increment = 2) { Value += value + increment; }
            }

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterInstance(instance: 5));
                    var state = new State { Value = 10 };
                    var context = container.For<State>();

                    context.Inject(ref state);
                    context.Inject(ref state, increment: 4);

                    return state.Value;
                }
            }
            """
        );

        Assert.That(Execute<int>(compilation), Is.EqualTo(26));
    }

    [Test]
    public void DependenciesWithoutIn_ShouldResolveOnceInDeclarationOrderBeforeCallingMethod()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using System.Collections.Generic;
            using Macaron.DependencyInjection;

            public class RecordingResolver : IDependencyResolver
            {
                public readonly List<string> Calls = new List<string>();

                public bool Contains<T>(object tag = null) => false;

                public T Resolve<T>(object tag = null)
                {
                    Calls.Add((string)tag);

                    return (T)(object)Calls.Count;
                }
            }

            public class Service
            {
                public string Result;

                [Inject] public void Initialize([Tag("first")] int first, [Tag("second")] int second)
                {
                    Result = $"{first}|{second}|{string.Join(",", Probe.Resolver.Calls)}";
                }
            }

            public static class Probe
            {
                public static readonly RecordingResolver Resolver = new RecordingResolver();

                public static string Run()
                {
                    var service = new Service();

                    Resolver.For<Service>().Inject(service);

                    return service.Result;
                }
            }
            """
        );

        Assert.That(Execute<string>(compilation), Is.EqualTo("1|2|first,second"));
    }

    [Test]
    public void BaseContext_ShouldUseBaseContractWithNormalVirtualAndHiddenMethodDispatch()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Base
            {
                public string Value;

                [Inject] public virtual void Initialize([Tag("base")] string value, [Assisted] string suffix = "base-default")
                {
                    Value = $"base:{value}:{suffix}";
                }
            }
            public class Overridden : Base
            {
                public override void Initialize(string value, string suffix = "override-default")
                {
                    Value = $"override:{value}:{suffix}";
                }
            }
            public class Hidden : Base
            {
                public new void Initialize(string value, string suffix = "hidden-default") { Value = "hidden"; }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                        registrar.RegisterInstance(instance: "dependency", tag: "base"));
                    var overridden = container.For<Overridden>().Create();
                    var hidden = container.For<Hidden>().Create();
                    var context = container.For<Base>();

                    context.Inject(overridden);
                    context.Inject(hidden);

                    return $"{overridden.Value}|{hidden.Value}";
                }
            }
            """
        );

        Assert.That(
            Execute<string>(compilation),
            Is.EqualTo("override:dependency:base-default|base:dependency:base-default")
        );
    }

    [Test]
    public void DerivedDirectDeclaration_ShouldHaveIndependentMethodAndConstructorPaths()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Base
            {
                public string Value;

                [Inject] public void Initialize() { Value = "base"; }
            }

            public class MethodDerived : Base
            {
                [Inject] public void Setup([Assisted] string value) { Value = value; }
            }

            public class ConstructorDerived : Base
            {
                [Inject] public ConstructorDerived([Assisted] string value) { Value = value; }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var method = new MethodDerived();

                    container.For<MethodDerived>().Inject(method, "method");

                    var constructor = container.For<ConstructorDerived>().Create("constructor");

                    return $"{method.Value}|{constructor.Value}";
                }
            }
            """
        );

        Assert.That(Execute<string>(compilation), Is.EqualTo("method|constructor"));
    }

    [Test]
    public void ExternalClosedGenericMethod_ShouldCompileAndExecuteInConsumerAssembly()
    {
        var external = EmitAssembly(CreateCompilation(
            """
            using Macaron.DependencyInjection;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]

            public class External<T>
            {
                public T Value;
                public string Name;

                [Inject] internal void Initialize(in T value, [Assisted] string name = "external")
                {
                    Value = value;
                    Name = name;
                }
            }
            """,
            assemblyName: "ExternalServices"
        ));
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterInstance(instance: 9));
                    var value = new External<int>();

                    container.For<External<int>>().Inject(value);

                    return $"{value.Value}|{value.Name}";
                }
            }
            """, additionalReferences: MetadataReference.CreateFromImage(external)
        );

        Assert.That(Execute<string>(compilation, external), Is.EqualTo("9|external"));
    }

    [Test]
    public void MissingDefaultedDependencies_ShouldUseExplicitDefaults()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable

            using Macaron.DependencyInjection;

            public class Service
            {
                public string? Value;

                [Inject] public void Initialize([Tag("absent")] string? text = "fallback", int? value = null)
                {
                    Value = $"{text}|{value.HasValue}";
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var service = new Service();

                    container.For<Service>().Inject(service);

                    return service.Value;
                }
            }
            """
        );

        Assert.That(Execute<string>(compilation), Is.EqualTo("fallback|False"));
    }

    [Test]
    public void ParamsDependency_ShouldResolveRegisteredArray()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public int Length;
                [Inject] public void Initialize(params string[] values) { Length = values.Length; }
            }

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                        registrar.RegisterInstance(instance: new[] { "a", "b" }));
                    var service = new Service();

                    container.For<Service>().Inject(service);

                    return service.Length;
                }
            }
            """
        );

        Assert.That(Execute<int>(compilation), Is.EqualTo(2));
    }

    [Test]
    public void NullableAnnotationsAndRepeatedForUses_ShouldShareOneGeneratedMethod()
    {
        var (compilation, sources) = Generate(
            """
            #nullable enable

            using Macaron.DependencyInjection;

            public class Service
            {
                [Inject] public void Initialize() { }
            }

            public class Configuration
            {
                public void Configure(IDependencyResolver resolver, Service service)
                {
                    resolver.For<Service>().Inject(service);
                    resolver.For<Service?>().Inject(service);
                    resolver.For<Service>().Inject(service);
                }
            }
            """
        );

        Assert.That(sources.Count(source => source.HintName.StartsWith("InjectionContextExtensions.")), Is.EqualTo(1));
        AssertCompiles(compilation);
    }

    [Test]
    public void DefaultContext_ShouldFailBeforeInvokingParameterlessMethod()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                public bool Called; [Inject] public void Initialize() { Called = true; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var service = new Service();

                    try { default(InjectionContext<Service>).Inject(service); }
                    catch (InvalidOperationException) { return !service.Called; }

                    return false;
                }

                public static void Discover(IDependencyResolver resolver) { resolver.For<Service>(); }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void InjectInsideRegisteredFactory_ShouldGenerateBeforeFinalBindingAndKeepStaticRegistrationType()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public interface IService
            {
                int Value { get; }
            }

            public class Service : IService
            {
                public int Value { get; private set; }

                [Inject] public void Initialize(int value) { Value = value; }
            }

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: 42);
                        registrar.RegisterSingleton<IService>((IDependencyResolver resolver, ref CleanupCollector collector) =>
                        {
                            var service = new Service();
                            resolver.For<Service>().Inject(service);
                            return service;
                        });
                    });

                    return container.Resolve<IService>().Value;
                }
            }
            """
        );

        Assert.That(Execute<int>(compilation), Is.EqualTo(42));
    }
}
