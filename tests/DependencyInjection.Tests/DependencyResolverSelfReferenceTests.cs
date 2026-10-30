// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyResolverSelfReferenceTests
{
    private sealed record Dependency(IDependencyResolver Resolver);

    private sealed record Consumer(IDependencyResolver Resolver, Dependency Dependency);

    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    [Test]
    public void DefaultTagAndLookupExtensions_ShouldReturnEachRequestingResolver()
    {
        using var root = DependencyContainerFactory.Create(_ => { });
        using var child = root.CreateChild();
        using var grandchild = child.CreateChild(_ => { });
        using var rootScope = root.CreateLocalScope();
        using var childScope = child.CreateLocalScope();
        using var siblingScope = child.CreateLocalScope();
        object? tag = null;
        var fallbackCalls = 0;
        Func<IDependencyResolver> fallback = () =>
        {
            fallbackCalls++;

            return root;
        };
        Func<IDependencyResolver, IDependencyResolver> resolverFallback = _ =>
        {
            fallbackCalls++;

            return root;
        };

        foreach (var resolver in new IDependencyResolver[] { root, child, grandchild, rootScope, childScope, siblingScope })
        {
            Assert.That(resolver.Contains<IDependencyResolver>(tag), Is.True);
            Assert.That(resolver.Contains<IDependencyResolver>(), Is.True);
            Assert.That(resolver.Resolve<IDependencyResolver>(tag), Is.SameAs(resolver));
            Assert.That(resolver.Resolve<IDependencyResolver>(), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrNull<IDependencyResolver>(tag), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrNull<IDependencyResolver>(), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse<IDependencyResolver>(fallback, tag: tag), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse(fallback), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse<IDependencyResolver>(resolverFallback, tag: tag), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse(resolverFallback), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse<IDependencyResolver>(root, tag: tag), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse((IDependencyResolver)root), Is.SameAs(resolver));
        }

        Assert.That(fallbackCalls, Is.Zero);
    }

    [Test]
    public void DefaultTagRegistration_ShouldFailBeforeFactoriesRun(
        [Values] Lifetime lifetime,
        [Values] bool useExplicitTag,
        [Values] bool registerInChild
    )
    {
        using var root = DependencyContainerFactory.Create(_ => { });
        var factoryCalls = 0;
        DependencyFactory<IDependencyResolver> factory = (resolver, ref _) =>
        {
            factoryCalls++;

            return resolver;
        };

        var error = Assert.Throws<ArgumentException>(() =>
        {
            using var container = registerInChild
                ? root.CreateChild(RegisterResolver)
                : DependencyContainerFactory.Create(RegisterResolver);
        });

        Assert.That(error!.ParamName, Is.EqualTo("key"));
        Assert.That(error.Message, Does.Contain(typeof(IDependencyResolver).ToString()));
        Assert.That(factoryCalls, Is.Zero);
        Assert.That(root.Resolve<IDependencyResolver>(), Is.SameAs(root));

        void RegisterResolver(IDependencyRegistrar registrar)
        {
            if (useExplicitTag)
            {
                registrar.Register<IDependencyResolver>(factory, lifetime, tag: null);
            }
            else
            {
                registrar.Register(factory, lifetime);
            }
        }
    }

    [Test]
    public void CustomResolverTags_ShouldFollowRegistrationAndCleanupRules(
        [Values] Lifetime lifetime,
        [Values("", "resolver")] string name
    )
    {
        var tag = name;
        var missingTag = "missing";
        var cleanupCalls = 0;
        using var parentResolver = DependencyContainerFactory.Create(_ => { });
        using var childResolver = DependencyContainerFactory.Create(_ => { });
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<IDependencyResolver>(() => parentResolver, lifetime, cleanup: _ => cleanupCalls++, tag: tag);
        });
        using var child = root.CreateChild(registrar =>
        {
            registrar.Register<IDependencyResolver>(() => childResolver, lifetime, cleanup: _ => cleanupCalls++, tag: tag);
        });
        using var scope = child.CreateLocalScope();

        Assert.That(root.Resolve<IDependencyResolver>(tag), Is.SameAs(parentResolver));
        Assert.That(child.Resolve<IDependencyResolver>(tag), Is.SameAs(childResolver));
        Assert.That(scope.Resolve<IDependencyResolver>(tag), Is.SameAs(childResolver));
        Assert.That(scope.Contains<IDependencyResolver>(tag), Is.True);
        Assert.That(scope.Contains<IDependencyResolver>(missingTag), Is.False);
        Assert.That(() => scope.Resolve<IDependencyResolver>(missingTag), Throws.InvalidOperationException);
        Assert.That(scope.ResolveOrNull<IDependencyResolver>(missingTag), Is.Null);
        Assert.That(scope.ResolveOrElse<IDependencyResolver>(parentResolver, tag: missingTag), Is.SameAs(parentResolver));
        Assert.That(scope.Resolve<IDependencyResolver>(), Is.SameAs(scope));

        scope.Dispose();
        child.Dispose();
        root.Dispose();

        Assert.That(cleanupCalls, Is.EqualTo(lifetime == Lifetime.Transient ? 3 : 2));
        Assert.That(parentResolver.Resolve<IDependencyResolver>(), Is.SameAs(parentResolver));
        Assert.That(childResolver.Resolve<IDependencyResolver>(), Is.SameAs(childResolver));
    }

    [Test]
    public void Factories_ShouldResolveSelfUsingTheirLifetimeContext(
        [Values] Lifetime lifetime,
        [Values] bool registerInChild
    )
    {
        var factoryCalls = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Dependency>(factory: (resolver, ref _) =>
            {
                return new Dependency(resolver.Resolve<IDependencyResolver>());
            });

            if (!registerInChild)
            {
                registrar.Register(CreateConsumer, lifetime);
            }
        });
        using var child = root.CreateChild(registrar =>
        {
            if (registerInChild)
            {
                registrar.Register(CreateConsumer, lifetime);
            }
        });
        var expectedInitializationCalls = lifetime == Lifetime.Transient ? 0 : 1;

        if (!registerInChild && lifetime == Lifetime.Scoped)
        {
            expectedInitializationCalls = 2;
        }

        Assert.That(factoryCalls, Is.EqualTo(expectedInitializationCalls));

        using var scope = child.CreateLocalScope();
        var direct = child.Resolve<Consumer>();
        var fromScope = scope.Resolve<Consumer>();
        var owner = !registerInChild && lifetime == Lifetime.Singleton ? root : child;
        IDependencyResolver scopeResolver = lifetime == Lifetime.Transient ? scope : owner;

        Assert.That(direct.Resolver, Is.SameAs(owner));
        Assert.That(direct.Dependency.Resolver, Is.SameAs(owner));
        Assert.That(fromScope.Resolver, Is.SameAs(scopeResolver));
        Assert.That(fromScope.Dependency.Resolver, Is.SameAs(scopeResolver));
        Assert.That(fromScope, lifetime == Lifetime.Transient ? Is.Not.SameAs(direct) : Is.SameAs(direct));

        Consumer CreateConsumer(IDependencyResolver resolver, ref CleanupCollector _)
        {
            var self = resolver.Resolve<IDependencyResolver>(null);

            Assert.That(self, Is.SameAs(resolver));

            factoryCalls++;

            return new Consumer(self, self.Resolve<Dependency>());
        }
    }

    [Test]
    public void ResolvedSelf_ShouldKeepLaterTransientResolutionsInRequestingScope()
    {
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(factory: () => new Resource());
            registrar.RegisterTransient<Dependency>(factory: (resolver, ref _) =>
            {
                return new Dependency(resolver.Resolve<IDependencyResolver>());
            });
        });
        using var child = root.CreateChild();
        using var firstScope = child.CreateLocalScope();
        using var secondScope = child.CreateLocalScope();
        var firstResolver = firstScope.Resolve<Dependency>().Resolver;
        var secondResolver = secondScope.Resolve<Dependency>().Resolver;
        var first = firstResolver.Resolve<Resource>();
        var second = secondResolver.Resolve<Resource>();
        var direct = child.Resolve<IDependencyResolver>().Resolve<Resource>();

        firstScope.Dispose();

        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.Zero);
        Assert.That(direct.DisposeCount, Is.Zero);
        Assert.That(secondResolver.Resolve<IDependencyResolver>(), Is.SameAs(secondScope));
        Assert.That(child.Resolve<IDependencyResolver>(), Is.SameAs(child));

        secondScope.Dispose();
        child.Dispose();

        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
        Assert.That(direct.DisposeCount, Is.EqualTo(1));
        Assert.That(root.Resolve<IDependencyResolver>(), Is.SameAs(root));
    }

    [Test]
    public void DisposedResolvers_ShouldRejectSelfLookupAndFallbacks([Values] bool disposeContainerFirst)
    {
        using var root = DependencyContainerFactory.Create(_ => { });
        using var child = root.CreateChild();
        using var scope = child.CreateLocalScope();
        var capturedResolver = scope.Resolve<IDependencyResolver>();
        object? tag = null;
        var fallbackCalls = 0;

        if (disposeContainerFirst)
        {
            child.Dispose();
        }
        else
        {
            scope.Dispose();
        }

        IDependencyResolver[] disposedResolvers = disposeContainerFirst ? [child, capturedResolver] : [capturedResolver];

        foreach (var resolver in disposedResolvers)
        {
            Assert.That(() => resolver.Resolve<IDependencyResolver>(tag), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => resolver.Resolve<IDependencyResolver>(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => resolver.Contains<IDependencyResolver>(tag), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => resolver.Contains<IDependencyResolver>(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => resolver.ResolveOrNull<IDependencyResolver>(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => resolver.ResolveOrElse<IDependencyResolver>(() =>
                {
                    fallbackCalls++;

                    return root;
                }),
                Throws.TypeOf<ObjectDisposedException>()
            );
        }

        using var replacement = (disposeContainerFirst ? root : child).CreateLocalScope();

        Assert.That(replacement.Resolve<IDependencyResolver>(), Is.SameAs(replacement));
        Assert.That(() => capturedResolver.Resolve<IDependencyResolver>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(root.Resolve<IDependencyResolver>(), Is.SameAs(root));
        Assert.That(fallbackCalls, Is.Zero);
    }
}
