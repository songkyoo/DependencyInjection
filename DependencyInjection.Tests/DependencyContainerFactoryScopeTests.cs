// ReSharper disable AccessToModifiedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerFactoryScopeTests
{
    private sealed class Dependency;

    [Test]
    public void ScopeCreationDuringFactory_ShouldThrowBeforeConfiguration_WithoutChangingOwnership(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime,
        [Values] bool resolveFromChild,
        [Values] bool useActionOverload
    )
    {
        var configurationCalls = 0;
        var factoryCalls = 0;
        var serviceCleanups = 0;
        var collectedCleanups = 0;
        var factoryContainers = new Dictionary<Dependency, IDependencyContainer>();
        using (var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Dependency>(
                factory: (resolver, ref collector) =>
                {
                    var container = (IDependencyContainer)resolver;
                    var previousConfigurationCalls = configurationCalls;

                    factoryCalls++;

                    Assert.That(container.Contains<Dependency>(), Is.True);
                    Assert.That(() => container.CreateScope(), Throws.InvalidOperationException);
                    Assert.That(container.CreateTransientScope, Throws.InvalidOperationException);

                    var exception = Assert.Throws<InvalidOperationException>(() =>
                    {
                        using var scope = useActionOverload
                            ? container.CreateScope(_ => configurationCalls++)
                            : container.CreateScope(new ActionDependencyInstaller(_ => configurationCalls++));
                    });

                    Assert.That(exception!.Message, Does.Contain("CreateScope"));
                    Assert.That(configurationCalls, Is.EqualTo(previousConfigurationCalls));
                    Assert.That(serviceCleanups, Is.Zero);
                    Assert.That(collectedCleanups, Is.Zero);

                    collector.Add(new object(), _ => collectedCleanups++);

                    var dependency = new Dependency();

                    factoryContainers.Add(dependency, container);

                    return dependency;
                },
                lifetime,
                cleanup: _ => serviceCleanups++
            );
        }))
        {
            using var child = root.CreateScope();
            var requestingContainer = resolveFromChild ? child : root;
            var first = requestingContainer.Resolve<Dependency>();
            var second = requestingContainer.Resolve<Dependency>();

            Assert.That(second, lifetime == Lifetime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));

            var expectedCreations = lifetime == Lifetime.Singleton ? 1 : 2;

            Assert.That(factoryCalls, Is.EqualTo(expectedCreations));

            var owningContainer = lifetime == Lifetime.Singleton ? root : requestingContainer;

            Assert.That(factoryContainers[first], Is.SameAs(owningContainer));
            Assert.That(factoryContainers[second], Is.SameAs(owningContainer));
            Assert.That(requestingContainer.Contains<Dependency>(), Is.True);
            Assert.That(serviceCleanups, Is.Zero);
            Assert.That(collectedCleanups, Is.Zero);

            using var scope = owningContainer.CreateScope(_ => configurationCalls++);
            using var transientScope = owningContainer.CreateTransientScope();

            Assert.That(configurationCalls, Is.EqualTo(1));
            Assert.That(transientScope.Contains<Dependency>(), Is.True);
        }

        Assert.That(serviceCleanups, Is.EqualTo(factoryCalls));
        Assert.That(collectedCleanups, Is.EqualTo(factoryCalls));
    }

    [Test]
    public void RejectedScopeCreation_ShouldCleanFailedFactory_AndAllowRetryAndLaterScopes(
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
                factory: (resolver, ref collector) =>
                {
                    var container = (IDependencyContainer)resolver;

                    factoryCalls++;
                    factoryContainer = container;

                    if (attemptScopeCreation)
                    {
                        collector.Add(new object(), _ =>
                        {
                            collectedCleanups++;

                            Assert.That(() => container.CreateScope(), Throws.InvalidOperationException);

                            if (cleanupFails)
                            {
                                throw cleanupFailure;
                            }
                        });

                        using var scope = container.CreateScope(_ => configurationCalled = true);
                    }

                    return new Dependency();
                },
                lifetime: Lifetime.Transient,
                cleanup: _ => serviceCleanups++
            );
        }))
        {
            using var child = root.CreateScope();
            var requestingContainer = resolveFromChild ? child : root;

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
