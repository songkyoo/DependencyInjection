using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class MethodInjectionLifetimeTests
{
    private const string Resources =
        """
        using System;
        using System.Collections.Generic;
        using Macaron.DependencyInjection;

        public class Resource : IDisposable
        {
            public int Disposals;

            public void Dispose() { Disposals++; }
        }

        public class Scoped : Resource { }

        public class Shared : Resource { }

        public class Service : Resource
        {
            public Resource Provided;
            public readonly List<Resource> Resolved = new List<Resource>();
            public Scoped Scoped;
            public Shared Shared;
            public IDependencyResolver Resolver;

            [Inject]
            public void Initialize(
                [Assisted] Resource provided,
                Resource resolved,
                Scoped scoped,
                Shared shared,
                IDependencyResolver resolver
            )
            {
                Provided = provided;
                Resolved.Add(resolved);
                Scoped = scoped;
                Shared = shared;
                Resolver = resolver;
            }
        }
        """;

    [Test]
    public void RepeatedRootInjection_ShouldResolveEachTimeAndKeepTargetAndProvidedResourceCallerOwned()
    {
        var compilation = CompileConsumer(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    var supplied = new Resource();
                    var target = new Service();
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>();
                        registrar.RegisterScoped<Scoped>();
                        registrar.RegisterSingleton<Shared>();
                    });

                    root.For<Service>().Inject(target, supplied);
                    root.For<Service>().Inject(target, supplied);

                    var correctDependencies = target.Resolved.Count == 2
                        && !ReferenceEquals(target.Resolved[0], target.Resolved[1])
                        && ReferenceEquals(target.Provided, supplied) && ReferenceEquals(target.Resolver, root)
                        && ReferenceEquals(target.Scoped, root.Resolve<Scoped>())
                        && ReferenceEquals(target.Shared, root.Resolve<Shared>());

                    root.Dispose();

                    return correctDependencies && target.Disposals == 0 && supplied.Disposals == 0
                        && target.Resolved[0].Disposals == 1 && target.Resolved[1].Disposals == 1
                        && target.Scoped.Disposals == 1 && target.Shared.Disposals == 1;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ChildInjection_ShouldUseChildScopedCacheAndLeaveParentResourcesAlive()
    {
        var compilation = CompileConsumer(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>();
                        registrar.RegisterScoped<Scoped>();
                        registrar.RegisterSingleton<Shared>();
                    });
                    using var child = root.CreateScope();
                    var parentScoped = root.Resolve<Scoped>();
                    var target = new Service();

                    child.For<Service>().Inject(target, new Resource());

                    var correctDependencies = ReferenceEquals(target.Resolver, child)
                        && ReferenceEquals(target.Scoped, child.Resolve<Scoped>())
                        && !ReferenceEquals(target.Scoped, parentScoped)
                        && ReferenceEquals(target.Shared, root.Resolve<Shared>());

                    child.Dispose();

                    return correctDependencies && target.Resolved[0].Disposals == 1 && target.Scoped.Disposals == 1
                        && parentScoped.Disposals == 0 && target.Shared.Disposals == 0 && target.Disposals == 0;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void TransientScopeInjection_ShouldOwnTransientsAndShareContainerCaches()
    {
        var compilation = CompileConsumer(
            """
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>();
                        registrar.RegisterScoped<Scoped>();
                        registrar.RegisterSingleton<Shared>();
                    });
                    using var child = root.CreateScope();
                    using var scope = child.CreateTransientScope();
                    var target = new Service();

                    scope.For<Service>().Inject(target, new Resource());

                    var correctDependencies = ReferenceEquals(target.Resolver, scope)
                        && ReferenceEquals(target.Scoped, child.Resolve<Scoped>())
                        && ReferenceEquals(target.Shared, root.Resolve<Shared>());

                    scope.Dispose();

                    return correctDependencies && target.Resolved[0].Disposals == 1
                        && target.Scoped.Disposals == 0 && target.Shared.Disposals == 0 && target.Disposals == 0;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void DependencyFailure_ShouldNotCallMethodOrUndoEarlierResolve()
    {
        var compilation = CompileConsumer(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Failing
            {
                public int Calls;

                [Inject] public void Initialize(Resource first, in string missing) { Calls++; }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var resource = new Resource();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() => resource);
                    });
                    using var scope = container.CreateTransientScope();
                    var target = new Failing();

                    try { scope.For<Failing>().Inject(target); }
                    catch (InvalidOperationException)
                    {
                        var retained = resource.Disposals == 0 && target.Calls == 0;
                        scope.Dispose();
                        return retained && resource.Disposals == 1;
                    }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void MethodFailure_ShouldPreserveExceptionPartialStateAndCompletedResolveOwnership()
    {
        var compilation = CompileConsumer(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Failing : Resource
            {
                public int Calls;
                [Inject] public void Initialize(Resource first, [Assisted] Resource supplied)
                {
                    Calls++;

                    throw Probe.Failure;
                }
            }

            public static class Probe
            {
                public static readonly Exception Failure = new Exception("method failed");

                public static bool Run()
                {
                    var supplied = new Resource();
                    var resolved = new Resource();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() => resolved);
                    });
                    using var scope = container.CreateTransientScope();
                    var target = new Failing();

                    try { scope.For<Failing>().Inject(target, supplied); }
                    catch (Exception error) when (ReferenceEquals(error, Failure))
                    {
                        var retained = resolved.Disposals == 0 && target.Calls == 1;

                        scope.Dispose();

                        return retained && resolved.Disposals == 1 && supplied.Disposals == 0 && target.Disposals == 0;
                    }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ThrowingStructMethod_ShouldKeepPartialMutationInOriginalStorage()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public struct State
            {
                public int Value;

                [Inject] public void Initialize([Assisted] int value) { Value = value; throw Probe.Failure; }
            }
            public static class Probe
            {
                public static readonly Exception Failure = new Exception("method failed");

                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var state = default(State);

                    try { container.For<State>().Inject(ref state, 42); }
                    catch (Exception error) when (ReferenceEquals(error, Failure)) { return state.Value == 42; }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void NullableAnnotationWithoutDefault_ShouldRequireRegistration()
    {
        var (compilation, _) = Generate(
            """
            #nullable enable

            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                public bool Called;

                [Inject] public void Initialize(string? required) { Called = true; }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });
                    var target = new Service();

                    try { container.For<Service>().Inject(target); }
                    catch (InvalidOperationException) { return !target.Called; }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void OptionalDependencyFactoryFailure_ShouldPropagateWithoutCallingMethodOrUsingDefault()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service
            {
                public bool Called;

                [Inject] public void Initialize(int value = 42) { Called = true; }
            }

            public static class Probe
            {
                public static readonly Exception Failure = new Exception("factory failed");

                public static bool Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                        registrar.RegisterTransient<int>(() => throw Failure));
                    var target = new Service();

                    try { container.For<Service>().Inject(target); }
                    catch (Exception error) when (ReferenceEquals(error, Failure)) { return !target.Called; }

                    return false;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    private static Compilation CompileConsumer(string source)
    {
        var input = CreateCompilation(source)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(Resources, InputParseOptions, "Resources.cs"));

        return Generate(input).Compilation;
    }
}
