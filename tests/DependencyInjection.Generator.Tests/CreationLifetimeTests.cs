using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.DependencyInjection.Testing.CompilationTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationLifetimeTests
{
    private const string Resources =
        """
        using System;
        using Macaron.DependencyInjection;

        public class Resource : IDisposable
        {
            public int Disposals;
            public void Dispose() { Disposals++; }
        }

        public class Shared : Resource { }
        public class Scoped : Resource { }

        public class Nested : Resource
        {
            public IDependencyResolver Resolver { get; }
            public Nested(IDependencyResolver resolver) { Resolver = resolver; }
        }

        public class Service : Resource
        {
            public Shared Shared { get; }
            public Scoped Scoped { get; }
            public Nested Nested { get; }
            public IDependencyResolver Resolver { get; }

            public Service(Shared shared, Scoped scoped, Nested nested, IDependencyResolver resolver)
            {
                Shared = shared;
                Scoped = scoped;
                Nested = nested;
                Resolver = resolver;
            }
        }
        """;

    [Test]
    public void CreateResults_ShouldBeFreshAndCallerOwned()
    {
        var (compilation, _) = Generate(
            """
            using System;
            using Macaron.DependencyInjection;

            public class Service : IDisposable
            {
                public int Disposals;
                public void Dispose() { Disposals++; }
            }

            public static class Probe
            {
                public static (bool, int, int) Run()
                {
                    var container = DependencyContainerFactory.Create(_ => { });
                    var first = container.For<Service>().Create();
                    var second = container.For<Service>().Create();
                    container.Dispose();

                    var automaticDisposals = first.Disposals + second.Disposals;
                    first.Dispose();
                    second.Dispose();

                    return (!ReferenceEquals(first, second), automaticDisposals,
                        first.Disposals + second.Disposals);
                }
            }
            """
        );

        Assert.That(Execute<(bool Distinct, int AutomaticDisposals, int CallerDisposals)>(compilation),
            Is.EqualTo((true, 0, 2)));
    }

    [Test]
    public void Create_ShouldNotCallInstanceMethodsAfterConstruction()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public int MethodCalls;
                public void Inject() { MethodCalls++; }
            }

            public static class Probe
            {
                public static int Run()
                {
                    using var container = DependencyContainerFactory.Create(_ => { });

                    return container.For<Service>().Create().MethodCalls;
                }
            }
            """
        );

        Assert.That(Execute<int>(compilation), Is.Zero);
    }

    [Test]
    public void RootCreate_ShouldReuseSharedCachesAndOwnResolvedTransients()
    {
        var compilation = CompileConsumer(
            """
            using System;
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<Shared>();
                        registrar.RegisterScoped<Scoped>();
                        registrar.RegisterTransient<Nested>();
                    });
                    IDependencyResolver resolver = root;
                    var first = resolver.For<Service>().Create();
                    var second = resolver.For<Service>().Create();
                    var correctDependencies = ReferenceEquals(first.Shared, root.Resolve<Shared>())
                        && ReferenceEquals(first.Scoped, root.Resolve<Scoped>())
                        && !ReferenceEquals(first.Nested, second.Nested)
                        && ReferenceEquals(first.Resolver, resolver)
                        && ReferenceEquals(first.Nested.Resolver, resolver);
                    var retainedUntilDisposal = first.Nested.Disposals == 0;
                    root.Dispose();

                    return correctDependencies && retainedUntilDisposal
                        && first.Shared.Disposals == 1 && first.Scoped.Disposals == 1
                        && first.Nested.Disposals == 1 && second.Nested.Disposals == 1
                        && first.Disposals == 0 && second.Disposals == 0;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void ChildCreate_ShouldUseLocalScopedCacheAndLeaveParentResourcesAlive()
    {
        var compilation = CompileConsumer(
            """
            using System;
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<Shared>();
                        registrar.RegisterScoped<Scoped>();
                        registrar.RegisterTransient<Nested>();
                    });
                    using var child = root.CreateChild();
                    IDependencyResolver resolver = child;
                    var service = resolver.For<Service>().Create();
                    var parentScoped = root.Resolve<Scoped>();
                    var correctDependencies = ReferenceEquals(service.Shared, root.Resolve<Shared>())
                        && ReferenceEquals(service.Scoped, child.Resolve<Scoped>())
                        && !ReferenceEquals(service.Scoped, parentScoped)
                        && ReferenceEquals(service.Resolver, resolver)
                        && ReferenceEquals(service.Nested.Resolver, resolver);
                    child.Dispose();

                    return correctDependencies && service.Shared.Disposals == 0 && parentScoped.Disposals == 0
                        && service.Scoped.Disposals == 1 && service.Nested.Disposals == 1 && service.Disposals == 0;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void LocalScopeCreate_ShouldShareContainerCachesAndCleanOnlyResolvedTransients()
    {
        var compilation = CompileConsumer(
            """
            using System;
            using Macaron.DependencyInjection;

            public static class Probe
            {
                public static bool Run()
                {
                    using var root = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterSingleton<Shared>();
                        registrar.RegisterScoped<Scoped>();
                        registrar.RegisterTransient<Nested>();
                    });
                    using var child = root.CreateChild();
                    using var scope = child.CreateLocalScope();
                    IDependencyResolver resolver = scope;
                    var service = resolver.For<Service>().Create();
                    var correctDependencies = ReferenceEquals(service.Shared, root.Resolve<Shared>())
                        && ReferenceEquals(service.Scoped, child.Resolve<Scoped>())
                        && !ReferenceEquals(service.Scoped, root.Resolve<Scoped>())
                        && ReferenceEquals(service.Resolver, resolver)
                        && ReferenceEquals(service.Nested.Resolver, resolver);
                    scope.Dispose();

                    return correctDependencies && service.Shared.Disposals == 0 && service.Scoped.Disposals == 0
                        && service.Nested.Disposals == 1 && service.Disposals == 0;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void RegisteredInstance_ShouldNotReplaceDirectConstruction()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public string Value { get; }
                public Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var registered = new Service("registered");
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: "direct");
                        registrar.RegisterInstance(instance: registered);
                    });
                    var created = container.For<Service>().Create();

                    return created.Value == "direct" && !ReferenceEquals(created, registered)
                        && ReferenceEquals(container.Resolve<Service>(), registered);
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [Test]
    public void UserFactory_ShouldNotRunDuringDirectConstruction()
    {
        var (compilation, _) = Generate(
            """
            using Macaron.DependencyInjection;

            public class Service
            {
                public string Value { get; }
                public Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static string Run()
                {
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterInstance(instance: "direct");
                        registrar.RegisterTransient<Service>(() => throw new System.Exception("User factory was called."));
                    });

                    return container.For<Service>().Create().Value;
                }
            }
            """
        );

        Assert.That(Execute<string>(compilation), Is.EqualTo("direct"));
    }

    [Test]
    public void ConstructorFailure_ShouldPreserveExceptionAndCompletedResolveOwnership()
    {
        var compilation = CompileConsumer(
            """
            using System;
            using System.Collections.Generic;
            using Macaron.DependencyInjection;

            public class FailingService
            {
                public FailingService(Resource resource, string last) { throw Probe.Failure; }
            }

            public static class Probe
            {
                public static readonly Exception Failure = new Exception("constructor failed");

                public static (
                    int BeforeRoot,
                    int BeforeScope,
                    int AfterScopeRoot,
                    int AfterScopeScope,
                    int AfterContainerRoot,
                    int AfterContainerScope) Run()
                {
                    var resources = new List<Resource>();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() =>
                        {
                            var resource = new Resource();
                            resources.Add(resource);
                            return resource;
                        });
                        registrar.RegisterInstance(instance: "last");
                    });
                    using var scope = container.CreateLocalScope();

                    try
                    {
                        container.For<FailingService>().Create();
                        throw new Exception("Creation should fail.");
                    }
                    catch (Exception error) when (ReferenceEquals(error, Failure)) { }

                    try
                    {
                        scope.For<FailingService>().Create();
                        throw new Exception("Creation should fail.");
                    }
                    catch (Exception error) when (ReferenceEquals(error, Failure)) { }

                    var rootDependency = resources[0];
                    var scopeDependency = resources[1];
                    var beforeRoot = rootDependency.Disposals;
                    var beforeScope = scopeDependency.Disposals;
                    scope.Dispose();
                    var afterScopeRoot = rootDependency.Disposals;
                    var afterScopeScope = scopeDependency.Disposals;
                    container.Dispose();

                    return (beforeRoot, beforeScope, afterScopeRoot, afterScopeScope,
                        rootDependency.Disposals, scopeDependency.Disposals);
                }
            }
            """
        );

        var disposals = Execute<(
            int BeforeRoot, int BeforeScope,
            int AfterScopeRoot, int AfterScopeScope,
            int AfterContainerRoot, int AfterContainerScope)>(compilation);

        Assert.Multiple(() =>
        {
            Assert.That((disposals.BeforeRoot, disposals.BeforeScope), Is.EqualTo((0, 0)));
            Assert.That((disposals.AfterScopeRoot, disposals.AfterScopeScope), Is.EqualTo((0, 1)));
            Assert.That((disposals.AfterContainerRoot, disposals.AfterContainerScope), Is.EqualTo((1, 1)));
        });
    }

    [Test]
    public void DependencyFailure_ShouldKeepEarlierResolvesOwnedWithoutCallingConstructor()
    {
        var compilation = CompileConsumer(
            """
            using System;
            using System.Collections.Generic;
            using Macaron.DependencyInjection;

            public class FailingService
            {
                public FailingService(Resource resource, string missing)
                {
                    throw new ApplicationException("Constructor must not run before every dependency is resolved.");
                }
            }

            public static class Probe
            {
                public static (
                    int BeforeRoot,
                    int BeforeScope,
                    int AfterScopeRoot,
                    int AfterScopeScope,
                    int AfterContainerRoot,
                    int AfterContainerScope) Run()
                {
                    var resources = new List<Resource>();
                    using var container = DependencyContainerFactory.Create(registrar =>
                    {
                        registrar.RegisterTransient<Resource>(() =>
                        {
                            var resource = new Resource();
                            resources.Add(resource);
                            return resource;
                        });
                    });
                    using var scope = container.CreateLocalScope();

                    try
                    {
                        container.For<FailingService>().Create();
                        throw new Exception("Resolution should fail.");
                    }
                    catch (InvalidOperationException) { }

                    try
                    {
                        scope.For<FailingService>().Create();
                        throw new Exception("Resolution should fail.");
                    }
                    catch (InvalidOperationException) { }

                    var rootDependency = resources[0];
                    var scopeDependency = resources[1];
                    var beforeRoot = rootDependency.Disposals;
                    var beforeScope = scopeDependency.Disposals;
                    scope.Dispose();
                    var afterScopeRoot = rootDependency.Disposals;
                    var afterScopeScope = scopeDependency.Disposals;
                    container.Dispose();

                    return (beforeRoot, beforeScope, afterScopeRoot, afterScopeScope,
                        rootDependency.Disposals, scopeDependency.Disposals);
                }
            }
            """
        );

        var disposals = Execute<(
            int BeforeRoot, int BeforeScope,
            int AfterScopeRoot, int AfterScopeScope,
            int AfterContainerRoot, int AfterContainerScope)>(compilation);

        Assert.Multiple(() =>
        {
            Assert.That((disposals.BeforeRoot, disposals.BeforeScope), Is.EqualTo((0, 0)));
            Assert.That((disposals.AfterScopeRoot, disposals.AfterScopeScope), Is.EqualTo((0, 1)));
            Assert.That((disposals.AfterContainerRoot, disposals.AfterContainerScope), Is.EqualTo((1, 1)));
        });
    }

    private static Compilation CompileConsumer(string source)
    {
        var input = CreateCompilation(source)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(Resources, InputParseOptions, "Resources.cs"));

        return Generate(input).Compilation;
    }
}
