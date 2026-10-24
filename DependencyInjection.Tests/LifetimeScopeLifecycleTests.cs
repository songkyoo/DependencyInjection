namespace Macaron.DependencyInjection.Tests;

public class LifetimeScopeLifecycleTests
{
    private sealed class Resource;

    private sealed record Service(Resource Resource);

    [Test]
    public void SharedAndTransientFactories_ShouldRejectLifetimeScopeCreation(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime,
        [Values] bool useChild
    )
    {
        var factoryCalls = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Resource>(factory: (resolver, ref _) =>
            {
                var container = (IDependencyContainer)resolver;
                factoryCalls++;

                Assert.That(container.CreateLifetimeScope, Throws.InvalidOperationException);
                Assert.That(resolver.Contains<Resource>(), Is.True);

                return new Resource();
            }, lifetime);
        });
        using var child = root.CreateScope(_ => { });
        var container = useChild ? child : root;
        container.Resolve<Resource>();

        var expectedCreations = lifetime == Lifetime.Scoped ? 2 : 1;

        Assert.That(factoryCalls, Is.EqualTo(expectedCreations));

        using var scope = container.CreateLifetimeScope();
        Assert.That(scope.Contains(Key<Resource>.Default), Is.True);
    }

    [Test]
    public void NestedFactoryAndFailedCollectorCleanup_ShouldRejectLifecycleChanges_WithoutInvalidatingScopes()
    {
        var fail = true;
        var failure = new ArgumentException("Dependency creation failed.");
        var cleanupCalls = 0;
        var installerCalls = 0;
        IDependencyContainer? container = null;
        LifetimeScope active = null!;
        LifetimeScope sibling = null!;
        container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(factory: (_, ref collector) =>
            {
                AssertChangesRejected();

                if (fail)
                {
                    collector.Add(new object(), _ =>
                    {
                        cleanupCalls++;
                        AssertChangesRejected();
                    });

                    throw failure;
                }

                return new Resource();
            });
            registrar.RegisterTransient<Service>(factory: (resolver, ref collector) =>
            {
                AssertChangesRejected();

                if (fail)
                {
                    collector.Add(new object(), _ =>
                    {
                        cleanupCalls++;
                        AssertChangesRejected();
                    });
                    var exception = Assert.Throws<ArgumentException>(() => resolver.Resolve<Resource>());

                    Assert.That(exception, Is.SameAs(failure));
                    AssertChangesRejected();
                }

                var resource = resolver.Resolve<Resource>();
                AssertChangesRejected();

                return new Service(resource);
            });
        });
        using (container)
        {
            active = container.CreateLifetimeScope();
            sibling = container.CreateLifetimeScope();
            var exception = Assert.Throws<ArgumentException>(() => active.Resolve(Key<Service>.Default));

            Assert.That(exception, Is.SameAs(failure));
            Assert.That(cleanupCalls, Is.EqualTo(3));
            Assert.That(installerCalls, Is.Zero);
            Assert.That(active.Contains(Key<Service>.Default), Is.True);
            Assert.That(sibling.Contains(Key<Service>.Default), Is.True);

            fail = false;
            Assert.That(active.Resolve(Key<Service>.Default).Resource, Is.Not.Null);

            active.Dispose();
            sibling.Dispose();
            using var replacement = container.CreateLifetimeScope();
            using var child = container.CreateScope(_ => installerCalls++);

            Assert.That(installerCalls, Is.EqualTo(1));
        }

        void AssertChangesRejected()
        {
            Assert.That(container!.CreateLifetimeScope, Throws.InvalidOperationException);
            Assert.That(() => container.CreateScope(_ => installerCalls++), Throws.InvalidOperationException);
            Assert.That(container.Dispose, Throws.InvalidOperationException);
            Assert.That(active.Dispose, Throws.InvalidOperationException);
            Assert.That(sibling.Dispose, Throws.InvalidOperationException);
            Assert.That(active.Contains(Key<Service>.Default), Is.True);
        }
    }

    [Test]
    public void ScopeCleanup_ShouldRejectOtherLifecycleChanges_AndAllowRepeatedOwnDisposal([Values] bool cleanupFails)
    {
        LifetimeScope active = null!;
        LifetimeScope sibling = null!;
        LifetimeScope ended = null!;
        IDependencyContainer? container = null;
        var cleanupCalls = 0;
        var installerCalls = 0;
        var failure = new ArgumentException("Cleanup failed.");
        container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Resource>();
            registrar.RegisterTransient<object>(cleanup: _ =>
            {
                cleanupCalls++;

                Assert.That(active.Dispose, Throws.Nothing);
                Assert.That(ended.Dispose, Throws.Nothing);
                Assert.That(() => active.Contains(Key<object>.Default), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(container!.CreateLifetimeScope, Throws.InvalidOperationException);
                Assert.That(() => container.CreateScope(_ => installerCalls++), Throws.InvalidOperationException);
                Assert.That(container.Dispose, Throws.InvalidOperationException);
                Assert.That(sibling.Dispose, Throws.InvalidOperationException);
                Assert.That(sibling.Resolve(Key<Resource>.Default), Is.SameAs(container.Resolve<Resource>()));

                if (cleanupFails)
                {
                    throw failure;
                }
            });
        });
        using (container)
        {
            active = container.CreateLifetimeScope();
            sibling = container.CreateLifetimeScope();
            ended = container.CreateLifetimeScope();
            ended.Dispose();
            active.Resolve(Key<object>.Default);

            if (cleanupFails)
            {
                var exception = Assert.Throws<AggregateException>(active.Dispose);
                Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { failure }));
            }
            else
            {
                Assert.That(active.Dispose, Throws.Nothing);
            }

            Assert.That(cleanupCalls, Is.EqualTo(1));
            Assert.That(installerCalls, Is.Zero);
            Assert.That(sibling.Contains(Key<object>.Default), Is.True);
            Assert.That(active.Dispose, Throws.Nothing);

            sibling.Dispose();
            using var replacement = container.CreateLifetimeScope();
            using var child = container.CreateScope(_ => installerCalls++);

            Assert.That(installerCalls, Is.EqualTo(1));
        }
    }
}
