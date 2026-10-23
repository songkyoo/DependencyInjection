// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToModifiedClosure
namespace Macaron.DependencyInjection.Tests;

public class CleanupCollectorTests
{
    private sealed class Resource(Action? onDispose = null) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            onDispose?.Invoke();
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(17)]
    public void CollectedEntries_ShouldSurviveArrayGrowth_AndRunBeforeService(int count)
    {
        var calls = new List<int>();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<object>(
                factory: (_, ref collector) =>
                {
                    for (var index = 0; index < count; index++)
                    {
                        collector.Add(index, instance => calls.Add((int)instance));
                    }
                    return new object();
                },
                cleanup: _ =>
                {
                    calls.Add(-1);
                }
            );
        });

        container.Resolve<object>();
        container.Resolve<object>();

        Assert.That(calls, Is.Empty);

        container.Dispose();

        Assert.That(calls, Is.EqualTo(Enumerable.Range(0, count).Reverse().Append(-1)));
    }

    [Test]
    public void FailedFactory_ShouldCleanCollectedEntries_PreserveError_AndAllowRetry(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values] bool cleanupFails
    )
    {
        var factoryFailure = new InvalidOperationException("Factory failed.");
        var cleanupFailure = new ArgumentException("Collected cleanup failed.");
        var failCreation = true;
        var calls = new List<string>();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<object>(
                factory: (_, ref collector) =>
                {
                    collector.Add(new Resource(() => calls.Add("first")));
                    collector.Add(new object(), _ =>
                    {
                        calls.Add("second");

                        if (cleanupFails)
                        {
                            throw cleanupFailure;
                        }
                    });

                    if (failCreation)
                    {
                        throw factoryFailure;
                    }

                    return new object();
                },
                lifeTime,
                cleanup: _ => calls.Add("service")
            );
        });

        if (cleanupFails)
        {
            var exception = Assert.Throws<AggregateException>(() => container.Resolve<object>());

            Assert.That(exception!.InnerExceptions, Is.EqualTo(new Exception[] { factoryFailure, cleanupFailure }));
        }
        else
        {
            var exception = Assert.Throws<InvalidOperationException>(() => container.Resolve<object>());

            Assert.That(exception, Is.SameAs(factoryFailure));
        }

        Assert.That(calls, Is.EqualTo(new[] { "second", "first" }));

        failCreation = false;

        var service = container.Resolve<object>();

        Assert.That(service, Is.Not.Null);

        if (cleanupFails)
        {
            var exception = Assert.Throws<AggregateException>(container.Dispose);

            Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { cleanupFailure }));
        }
        else
        {
            container.Dispose();
        }

        Assert.That(calls, Is.EqualTo(new[] { "second", "first", "second", "first", "service" }));
        Assert.That(container.Dispose, Throws.Nothing);
    }

    [Test]
    public void InvalidFactoryResult_ShouldCleanCollectedEntries_WithoutRunningServiceCleanup(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values] bool wrongType
    )
    {
        var resource = new Resource();
        var serviceCleanupCount = 0;
        DependencyFactory<object> factory = (_, ref collector) =>
        {
            collector.Add(resource);

            return wrongType ? "invalid" : null!;
        };

        using var container = DependencyContainerFactory.Create(registrar =>
        {
            switch (lifeTime)
            {
                case LifeTime.Transient:
                    registrar.RegisterTransient(typeof(Resource), null, factory, _ => serviceCleanupCount++);
                    break;
                case LifeTime.Scoped:
                    registrar.RegisterScoped(typeof(Resource), null, factory, _ => serviceCleanupCount++);
                    break;
                case LifeTime.Singleton:
                    registrar.RegisterSingleton(typeof(Resource), null, factory, _ => serviceCleanupCount++);
                    break;
            }
        });

        Assert.That(container.Resolve<Resource>, Throws.InvalidOperationException);
        Assert.That(resource.DisposeCount, Is.EqualTo(1));
        Assert.That(serviceCleanupCount, Is.Zero);

        container.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void CollectedEntries_ShouldFollowFactoryOwnership(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var cleanupCount = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<object>(
                factory: (_, ref collector) =>
                {
                    collector.Add(new object(), _ => cleanupCount++);

                    return new object();
                },
                lifeTime
            );
        });
        using var child = root.CreateScope();
        using var sibling = root.CreateScope();

        child.Resolve<object>();
        sibling.Resolve<object>();
        child.Dispose();

        Assert.That(cleanupCount, Is.EqualTo(lifeTime == LifeTime.Singleton ? 0 : 1));

        sibling.Dispose();

        Assert.That(cleanupCount, Is.EqualTo(lifeTime == LifeTime.Singleton ? 0 : 2));

        root.Dispose();

        Assert.That(cleanupCount, Is.EqualTo(lifeTime == LifeTime.Singleton ? 1 : 2));
    }

    [Test]
    public void FailedFactory_ShouldLeaveResolvedDependencyOwnedByContainer()
    {
        var dependency = new Resource();
        var collected = new Resource();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped(factory: () => dependency);
            registrar.RegisterSingleton<object>(factory: (resolver, ref collector) =>
            {
                collector.Add(collected);
                resolver.Resolve<Resource>();

                throw new InvalidOperationException("Factory failed.");
            });
        });

        Assert.That(container.Resolve<object>, Throws.InvalidOperationException);
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(dependency.DisposeCount, Is.Zero);
        Assert.That(container.Resolve<Resource>(), Is.SameAs(dependency));

        container.Dispose();

        Assert.That(dependency.DisposeCount, Is.EqualTo(1));
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void CollectedCustomCleanup_ShouldReplaceDispose()
    {
        var resource = new Resource();
        var cleanupCount = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<object>(factory: (_, ref collector) =>
            {
                collector.Add(resource, _ => cleanupCount++);
                collector.Add(resource, _ => { });

                return new object();
            });
        });

        container.Resolve<object>();
        container.Dispose();

        Assert.That(resource.DisposeCount, Is.Zero);
        Assert.That(cleanupCount, Is.EqualTo(1));
    }
}
