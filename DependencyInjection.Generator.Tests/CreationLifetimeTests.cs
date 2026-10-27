using static Macaron.DependencyInjection.Generator.Tests.GeneratorTestHelper;

namespace Macaron.DependencyInjection.Generator.Tests;

public class CreationLifetimeTests
{
    [TestCase("root")]
    [TestCase("child")]
    [TestCase("transient")]
    public void Create_ShouldPreserveResolverCachesAndOwnOnlyResolvedDependencies(string context)
    {
        var (compilation, _) = Generate(
            $$"""
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
                public IDependencyResolver Resolver;

                public Nested(IDependencyResolver resolver)
                {
                    Resolver = resolver;
                }
            }

            public class Service : Resource
            {
                public Shared Shared;
                public Scoped Scoped;
                public Nested Nested;
                public IDependencyResolver Resolver;
                public int MethodCalls;

                public Service(Shared shared, Scoped scoped, Nested nested, IDependencyResolver resolver)
                {
                    Shared = shared;
                    Scoped = scoped;
                    Nested = nested;
                    Resolver = resolver;
                }

                public void Inject() { MethodCalls++; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var root = DependencyContainerFactory.Create(r =>
                    {
                        r.RegisterSingleton<Shared>();
                        r.RegisterScoped<Scoped>();
                        r.RegisterTransient<Nested>();
                    });
                    var child = root.CreateScope();
                    var scope = child.CreateTransientScope();
                    IDependencyResolver resolver = {{(context == "root" ? "root" : context == "child" ? "child" : "scope")}};
                    var first = resolver.For<Service>().Create();
                    var second = resolver.For<Service>().Create();
                    var valid = !ReferenceEquals(first, second)
                        && ReferenceEquals(first.Shared, root.Resolve<Shared>())
                        && ReferenceEquals(first.Scoped, resolver.Resolve<Scoped>())
                        && ReferenceEquals(first.Resolver, resolver)
                        && ReferenceEquals(first.Nested.Resolver, resolver)
                        && !ReferenceEquals(first.Nested, second.Nested)
                        && first.MethodCalls == 0;

                    if ({{(context != "root").ToString().ToLowerInvariant()}})
                        valid &= !ReferenceEquals(first.Scoped, root.Resolve<Scoped>());

                    scope.Dispose();

                    valid &= first.Nested.Disposals == {{(context == "transient" ? "1" : "0")}}
                        && first.Shared.Disposals == 0 && first.Scoped.Disposals == 0;

                    child.Dispose();

                    valid &= first.Nested.Disposals == {{(context == "root" ? "0" : "1")}}
                        && first.Scoped.Disposals == {{(context == "root" ? "0" : "1")}};

                    root.Dispose();

                    valid &= first.Shared.Disposals == 1 && first.Scoped.Disposals == 1
                        && first.Nested.Disposals == 1 && first.Disposals == 0 && second.Disposals == 0;

                    first.Dispose(); second.Dispose();

                    return valid && first.Disposals == 1 && second.Disposals == 1;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Create_ShouldIgnoreRegisteredInstanceAndUserFactory(bool factory)
    {
        var (compilation, _) = Generate(
            $$"""
            using Macaron.DependencyInjection;

            public class Service
            {
                public string Value;

                public Service(string value) { Value = value; }
            }

            public static class Probe
            {
                public static bool Run()
                {
                    var calls = 0;
                    var registered = new Service("registered");
                    using var container = DependencyContainerFactory.Create(r =>
                    {
                        r.RegisterSingleton(instance: "direct");
                        {{(factory ? "r.RegisterTransient<Service>(() => { calls++; return registered; });" : "r.RegisterSingleton(instance: registered);")}}
                    });
                    var first = container.For<Service>().Create();
                    var second = container.For<Service>().Create();

                    return first.Value == "direct" && second.Value == "direct" && calls == 0
                        && !ReferenceEquals(first, registered) && !ReferenceEquals(first, second);
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void CreationFailure_ShouldRetainCompletedResolvesUntilOwnerDisposal(bool transientScope, bool constructorFailure)
    {
        var (compilation, _) = Generate(
            $$"""
            using System;
            using Macaron.DependencyInjection;

            public class Resource : IDisposable
            {
                public int Disposals;

                public void Dispose() { Disposals++; }
            }

            public class Service
            {
                public Service(Resource resource, string last) { throw Probe.Failure; }
            }

            public static class Probe
            {
                public static readonly Exception Failure = new Exception("constructor failed");

                public static bool Run()
                {
                    Resource resolved = null;
                    var container = DependencyContainerFactory.Create(r =>
                    {
                        r.RegisterTransient<Resource>(() => resolved = new Resource());
                        {{(constructorFailure ? "r.RegisterSingleton(instance: \"last\");" : "")}}
                    });
                    var scope = container.CreateTransientScope();
                    IDependencyResolver resolver = {{(transientScope ? "scope" : "container")}};
                    var valid = false;

                    try { resolver.For<Service>().Create(); }
                    catch (Exception error)
                    {
                        valid = {{(constructorFailure ? "ReferenceEquals(error, Failure)" : "error is InvalidOperationException")}}
                            && resolved != null && resolved.Disposals == 0;
                    }

                    scope.Dispose();

                    valid &= resolved.Disposals == {{(transientScope ? "1" : "0")}};

                    container.Dispose();

                    return valid && resolved.Disposals == 1;
                }
            }
            """
        );

        Assert.That(Execute<bool>(compilation), Is.True);
    }
}
