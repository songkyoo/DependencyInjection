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
    public void DefaultKeyAndLookupExtensions_ShouldReturnEachRequestingResolver()
    {
        using var root = DependencyContainerFactory.Create(_ => { });
        using var child = root.CreateScope();
        using var grandchild = child.CreateScope(_ => { });
        using var rootScope = root.CreateTransientScope();
        using var childScope = child.CreateTransientScope();
        using var siblingScope = child.CreateTransientScope();
        var key = Key<IDependencyResolver>.Default;
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
            Assert.That(resolver.Contains(key), Is.True);
            Assert.That(resolver.Contains<IDependencyResolver>(), Is.True);
            Assert.That(resolver.Resolve(key), Is.SameAs(resolver));
            Assert.That(resolver.Resolve<IDependencyResolver>(), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrNull(key), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrNull<IDependencyResolver>(), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse(key, fallback), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse(fallback), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse(key, resolverFallback), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse(resolverFallback), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse(key, root), Is.SameAs(resolver));
            Assert.That(resolver.ResolveOrElse((IDependencyResolver)root), Is.SameAs(resolver));
        }

        Assert.That(fallbackCalls, Is.Zero);
    }

    [Test]
    public void DefaultKeyRegistration_ShouldFailBeforeFactoriesRun(
        [Values] Lifetime lifetime,
        [Values] bool useExplicitKey,
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
                ? root.CreateScope(RegisterResolver)
                : DependencyContainerFactory.Create(RegisterResolver);
        });

        Assert.That(error!.ParamName, Is.EqualTo("key"));
        Assert.That(error.Message, Does.Contain(Key<IDependencyResolver>.Default.ToString()));
        Assert.That(factoryCalls, Is.Zero);
        Assert.That(root.Resolve<IDependencyResolver>(), Is.SameAs(root));

        void RegisterResolver(IDependencyRegistrar registrar)
        {
            if (useExplicitKey)
            {
                registrar.Register(Key<IDependencyResolver>.Default, factory, lifetime);
            }
            else
            {
                registrar.Register(factory, lifetime);
            }
        }
    }

    [Test]
    public void CustomResolverKeys_ShouldFollowRegistrationAndCleanupRules(
        [Values] Lifetime lifetime,
        [Values("", "resolver")] string name
    )
    {
        var key = new Key<IDependencyResolver>(name);
        var missingKey = new Key<IDependencyResolver>(name);
        var cleanupCalls = 0;
        using var parentResolver = DependencyContainerFactory.Create(_ => { });
        using var childResolver = DependencyContainerFactory.Create(_ => { });
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, () => parentResolver, lifetime, _ => cleanupCalls++);
        });
        using var child = root.CreateScope(registrar =>
        {
            registrar.Register(key, () => childResolver, lifetime, _ => cleanupCalls++);
        });
        using var scope = child.CreateTransientScope();

        Assert.That(root.Resolve(key), Is.SameAs(parentResolver));
        Assert.That(child.Resolve(key), Is.SameAs(childResolver));
        Assert.That(scope.Resolve(key), Is.SameAs(childResolver));
        Assert.That(scope.Contains(key), Is.True);
        Assert.That(scope.Contains(missingKey), Is.False);
        Assert.That(() => scope.Resolve(missingKey), Throws.InvalidOperationException);
        Assert.That(scope.ResolveOrNull(missingKey), Is.Null);
        Assert.That(scope.ResolveOrElse(missingKey, parentResolver), Is.SameAs(parentResolver));
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
        using var child = root.CreateScope(registrar =>
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

        using var scope = child.CreateTransientScope();
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
            var self = resolver.Resolve(Key<IDependencyResolver>.Default);

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
            registrar.RegisterTransient<Resource>();
            registrar.RegisterTransient<Dependency>(factory: (resolver, ref _) =>
            {
                return new Dependency(resolver.Resolve<IDependencyResolver>());
            });
        });
        using var child = root.CreateScope();
        using var firstScope = child.CreateTransientScope();
        using var secondScope = child.CreateTransientScope();
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
        using var child = root.CreateScope();
        using var scope = child.CreateTransientScope();
        var capturedResolver = scope.Resolve<IDependencyResolver>();
        var key = Key<IDependencyResolver>.Default;
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
            Assert.That(() => resolver.Resolve(key), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(resolver.Resolve<IDependencyResolver>, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => resolver.Contains(key), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(resolver.Contains<IDependencyResolver>, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(resolver.ResolveOrNull<IDependencyResolver>, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => resolver.ResolveOrElse<IDependencyResolver>(() =>
                {
                    fallbackCalls++;

                    return root;
                }),
                Throws.TypeOf<ObjectDisposedException>()
            );
        }

        using var replacement = (disposeContainerFirst ? root : child).CreateTransientScope();

        Assert.That(replacement.Resolve<IDependencyResolver>(), Is.SameAs(replacement));
        Assert.That(capturedResolver.Resolve<IDependencyResolver>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(root.Resolve<IDependencyResolver>(), Is.SameAs(root));
        Assert.That(fallbackCalls, Is.Zero);
    }
}
