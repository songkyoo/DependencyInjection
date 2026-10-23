// ReSharper disable AccessToModifiedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerFactoryScopeTests
{
    private sealed class Dependency;

    private sealed class Service(Dependency dependency)
    {
        public Dependency Dependency { get; } = dependency;
    }

    [Test]
    public void CreateScopeDuringFactory_ShouldThrowBeforeConfiguration_WithoutChangingOwnership(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values] bool resolveFromChild,
        [Values] bool configureScope
    )
    {
        var configurationCalled = false;
        var factoryCalls = 0;
        var serviceCleanups = 0;
        var collectedCleanups = 0;
        IDependencyContainer? factoryContainer = null;
        using (var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Dependency>(
                factory: (container, ref collector) =>
                {
                    factoryCalls++;
                    factoryContainer = container;

                    Assert.That(container.Contains<Dependency>(), Is.True);

                    var exception = Assert.Throws<InvalidOperationException>(() =>
                    {
                        using var scope = configureScope
                            ? container.CreateScope(_ => configurationCalled = true)
                            : container.CreateScope();
                    });

                    Assert.That(exception!.Message, Does.Contain("CreateScope"));
                    Assert.That(configurationCalled, Is.False);
                    Assert.That(serviceCleanups, Is.Zero);
                    Assert.That(collectedCleanups, Is.Zero);

                    collector.Add(new object(), _ => collectedCleanups++);

                    return new Dependency();
                },
                lifeTime,
                cleanup: _ => serviceCleanups++
            );
        }))
        {
            using var child = root.CreateScope();
            var requestingContainer = resolveFromChild ? child : (IDependencyContainer)root;
            var first = requestingContainer.Resolve<Dependency>();
            var second = requestingContainer.Resolve<Dependency>();

            Assert.That(second, lifeTime == LifeTime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));
            Assert.That(factoryCalls, Is.EqualTo(lifeTime == LifeTime.Transient ? 2 : 1));
            Assert.That(factoryContainer, Is.SameAs(lifeTime == LifeTime.Singleton ? root : requestingContainer));
            Assert.That(requestingContainer.Contains<Dependency>(), Is.True);
            Assert.That(serviceCleanups, Is.Zero);
            Assert.That(collectedCleanups, Is.Zero);

            using var scope = factoryContainer!.CreateScope(_ => configurationCalled = true);

            Assert.That(configurationCalled, Is.True);
        }

        Assert.That(serviceCleanups, Is.EqualTo(factoryCalls));
        Assert.That(collectedCleanups, Is.EqualTo(factoryCalls));
    }

    [Test]
    public void CreateScopeAfterNestedResolve_ShouldRemainBlockedUntilOuterFactoryReturns(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Dependency>(
                factory: (container, ref _) =>
                {
                    Assert.That(container.CreateScope, Throws.InvalidOperationException);

                    return new Dependency();
                },
                lifeTime
            );
            registrar.Register<Service>(
                factory: (container, ref _) =>
                {
                    Assert.That(container.Contains<Dependency>(), Is.True);

                    var dependency = container.Resolve<Dependency>();

                    Assert.That(container.CreateScope, Throws.InvalidOperationException);

                    return new Service(dependency);
                },
                lifeTime
            );
        });
        var service = container.Resolve<Service>();

        Assert.That(service.Dependency, Is.Not.Null);

        using var scope = container.CreateScope();

        Assert.That(scope.Contains<Service>(), Is.True);
    }

    [Test]
    public void RejectedScopeCreation_ShouldCleanFailedFactory_AndAllowRetryAndLaterScopes(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values] bool resolveFromChild,
        [Values] bool cleanupFails
    )
    {
        var attemptScopeCreation = true;
        var configurationCalled = false;
        var factoryCalls = 0;
        var serviceCleanups = 0;
        var collectedCleanups = 0;
        var cleanupFailure = new ArgumentException("Collected cleanup failed.");
        IDependencyContainer? factoryContainer = null;
        using (var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Dependency>(
                factory: (container, ref collector) =>
                {
                    factoryCalls++;
                    factoryContainer = container;

                    if (attemptScopeCreation)
                    {
                        collector.Add(new object(), _ =>
                        {
                            collectedCleanups++;

                            Assert.That(container.CreateScope, Throws.InvalidOperationException);

                            if (cleanupFails)
                            {
                                throw cleanupFailure;
                            }
                        });

                        using var scope = container.CreateScope(_ => configurationCalled = true);
                    }

                    return new Dependency();
                },
                lifeTime,
                cleanup: _ => serviceCleanups++
            );
        }))
        {
            using var child = root.CreateScope();
            var requestingContainer = resolveFromChild ? child : (IDependencyContainer)root;

            if (cleanupFails)
            {
                var exception = Assert.Throws<AggregateException>(() => requestingContainer.Resolve<Dependency>());

                Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
                Assert.That(exception.InnerExceptions[0].Message, Does.Contain("CreateScope"));
                Assert.That(exception.InnerExceptions[1], Is.SameAs(cleanupFailure));
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>(() => requestingContainer.Resolve<Dependency>());

                Assert.That(exception!.Message, Does.Contain("CreateScope"));
            }

            Assert.That(configurationCalled, Is.False);
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(serviceCleanups, Is.Zero);
            Assert.That(collectedCleanups, Is.EqualTo(1));

            using var scope = factoryContainer!.CreateScope();

            attemptScopeCreation = false;

            Assert.That(requestingContainer.Resolve<Dependency>(), Is.Not.Null);
            Assert.That(factoryCalls, Is.EqualTo(2));
            Assert.That(serviceCleanups, Is.Zero);
        }

        Assert.That(serviceCleanups, Is.EqualTo(1));
        Assert.That(collectedCleanups, Is.EqualTo(1));
    }
}
