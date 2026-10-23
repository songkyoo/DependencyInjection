// ReSharper disable AccessToModifiedClosure
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerFactoryDisposalTests
{
    private sealed class Dependency;

    private sealed class Service(Dependency dependency)
    {
        public Dependency Dependency { get; } = dependency;
    }

    [Test]
    public void DisposeDuringFactory_ShouldThrowWithoutChangingRegistrationsCachesOrCleanups(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values] bool resolveFromChild
    )
    {
        var factoryCalls = 0;
        var dependencyCleanups = 0;
        var serviceCleanups = 0;
        var collectedCleanups = 0;
        using (var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Dependency>(cleanup: _ => dependencyCleanups++);
            registrar.Register<Service>(
                factory: (container, ref collector) =>
                {
                    factoryCalls++;

                    var dependency = container.Resolve<Dependency>();
                    var exception = Assert.Throws<InvalidOperationException>(container.Dispose);

                    Assert.That(exception!.Message, Does.Contain("Dispose"));
                    Assert.That(container.Contains<Service>(), Is.True);
                    Assert.That(container.Contains<Dependency>(), Is.True);
                    Assert.That(container.Resolve<Dependency>(), Is.SameAs(dependency));
                    Assert.That(dependencyCleanups, Is.Zero);
                    Assert.That(serviceCleanups, Is.Zero);
                    Assert.That(collectedCleanups, Is.Zero);

                    collector.Add(new object(), _ => collectedCleanups++);

                    return new Service(dependency);
                },
                lifeTime,
                cleanup: _ => serviceCleanups++
            );
        }))
        {
            using var child = root.CreateScope(_ => { });
            var requestingContainer = resolveFromChild ? child : root;
            var owningContainer = lifeTime == LifeTime.Singleton ? root : requestingContainer;
            var dependency = owningContainer.Resolve<Dependency>();
            var first = requestingContainer.Resolve<Service>();
            var second = requestingContainer.Resolve<Service>();

            Assert.That(first.Dependency, Is.SameAs(dependency));
            Assert.That(second.Dependency, Is.SameAs(dependency));
            Assert.That(second, lifeTime == LifeTime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));
            var expectedCreations = lifeTime == LifeTime.Singleton ? 1 : 2;

            Assert.That(factoryCalls, Is.EqualTo(expectedCreations));

            using var scope = owningContainer.CreateScope(_ => { });

            Assert.That(scope.Contains<Service>(), Is.True);
        }

        Assert.That(dependencyCleanups, Is.EqualTo(3));
        Assert.That(serviceCleanups, Is.EqualTo(factoryCalls));
        Assert.That(collectedCleanups, Is.EqualTo(factoryCalls));
    }

    [Test]
    public void DisposeAfterNestedResolve_ShouldRemainBlockedUntilOuterFactoryReturns(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values] bool dependencyFails
    )
    {
        var failDependency = dependencyFails;
        var dependencyFailure = new ArgumentException("Dependency creation failed.");
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Dependency>(
                factory: (container, ref _) =>
                {
                    Assert.That(container.Dispose, Throws.InvalidOperationException);

                    if (failDependency)
                    {
                        failDependency = false;
                        throw dependencyFailure;
                    }

                    return new Dependency();
                },
                LifeTime.Transient
            );
            registrar.Register<Service>(
                factory: (container, ref _) =>
                {
                    if (dependencyFails)
                    {
                        failDependency = true;

                        var exception = Assert.Throws<ArgumentException>(() => container.Resolve<Dependency>());

                        Assert.That(exception, Is.SameAs(dependencyFailure));
                        Assert.That(container.Dispose, Throws.InvalidOperationException);
                    }

                    var dependency = container.Resolve<Dependency>();

                    Assert.That(container.Dispose, Throws.InvalidOperationException);

                    return new Service(dependency);
                },
                lifeTime
            );
        });
        var service = container.Resolve<Service>();

        Assert.That(service.Dependency, Is.Not.Null);

        using var scope = container.CreateScope(_ => { });

        Assert.That(scope.Contains<Service>(), Is.True);
    }

    [Test]
    public void RejectedDisposal_ShouldCleanFailedFactory_AndAllowRetryOrNormalDisposal(
        [Values] bool resolveFromChild,
        [Values] bool cleanupFails,
        [Values] bool retryCreation
    )
    {
        var attemptDisposal = true;
        var factoryCalls = 0;
        var dependencyCleanups = 0;
        var serviceCleanups = 0;
        var collectedCleanups = 0;
        var cleanupFailure = new ArgumentException("Collected cleanup failed.");
        Dependency? resolvedDependency = null;
        IDependencyContainer? factoryContainer = null;
        using (var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Dependency>(cleanup: _ => dependencyCleanups++);
            registrar.Register<Service>(
                factory: (container, ref collector) =>
                {
                    factoryCalls++;
                    factoryContainer = container;
                    resolvedDependency = container.Resolve<Dependency>();

                    if (attemptDisposal)
                    {
                        collector.Add(new object(), _ =>
                        {
                            collectedCleanups++;

                            Assert.That(container.Dispose, Throws.InvalidOperationException);

                            if (cleanupFails)
                            {
                                throw cleanupFailure;
                            }
                        });

                        container.Dispose();
                    }

                    return new Service(resolvedDependency);
                },
                LifeTime.Transient,
                cleanup: _ => serviceCleanups++
            );
        }))
        {
            using var child = root.CreateScope(_ => { });
            var requestingContainer = resolveFromChild ? child : root;
            var owningContainer = requestingContainer;

            if (cleanupFails)
            {
                var exception = Assert.Throws<AggregateException>(() => requestingContainer.Resolve<Service>());

                Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
                Assert.That(exception.InnerExceptions[0].Message, Does.Contain("Dispose"));
                Assert.That(exception.InnerExceptions[1], Is.SameAs(cleanupFailure));
            }
            else
            {
                var exception = Assert.Throws<InvalidOperationException>(() => requestingContainer.Resolve<Service>());

                Assert.That(exception!.Message, Does.Contain("Dispose"));
            }

            Assert.That(factoryContainer, Is.SameAs(owningContainer));
            Assert.That(owningContainer.Contains<Service>(), Is.True);
            Assert.That(owningContainer.Resolve<Dependency>(), Is.SameAs(resolvedDependency));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(dependencyCleanups, Is.Zero);
            Assert.That(serviceCleanups, Is.Zero);
            Assert.That(collectedCleanups, Is.EqualTo(1));

            using var scope = owningContainer.CreateScope(_ => { });

            if (retryCreation)
            {
                attemptDisposal = false;

                var service = requestingContainer.Resolve<Service>();

                Assert.That(service.Dependency, Is.SameAs(resolvedDependency));
                Assert.That(factoryCalls, Is.EqualTo(2));
                Assert.That(serviceCleanups, Is.Zero);
            }
        }

        Assert.That(dependencyCleanups, Is.EqualTo(3));
        Assert.That(serviceCleanups, Is.EqualTo(retryCreation ? 1 : 0));
        Assert.That(collectedCleanups, Is.EqualTo(1));
    }
}
