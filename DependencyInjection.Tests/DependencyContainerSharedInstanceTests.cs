// ReSharper disable DisposeOnUsingVariable
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerSharedInstanceTests
{
    private interface IFirst;

    private interface ISecond;

    private sealed class Resource : IFirst, ISecond, IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    [Test]
    public void RepeatedCollectedInstance_ShouldApplyEachCleanupPolicy(
        [Values] bool firstUsesDefault,
        [Values] bool secondUsesDefault
    )
    {
        var resource = new Resource();
        var calls = new List<string>();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(factory: (_, ref collector) =>
            {
                if (firstUsesDefault)
                {
                    collector.Add(resource);
                }
                else
                {
                    collector.Add(resource, _ => calls.Add("first"));
                }

                if (secondUsesDefault)
                {
                    collector.Add(resource);
                }
                else
                {
                    collector.Add(resource, _ => calls.Add("second"));
                }

                return new object();
            });
        });

        container.Resolve<object>();

        Assert.That(resource.DisposeCount, Is.Zero);
        Assert.That(calls, Is.Empty);

        container.Dispose();
        container.Dispose();

        var expectedCalls = new List<string>();

        if (!secondUsesDefault)
        {
            expectedCalls.Add("second");
        }

        if (!firstUsesDefault)
        {
            expectedCalls.Add("first");
        }

        var expectedDisposeCount = (firstUsesDefault ? 1 : 0) + (secondUsesDefault ? 1 : 0);

        Assert.That(calls, Is.EqualTo(expectedCalls));
        Assert.That(resource.DisposeCount, Is.EqualTo(expectedDisposeCount));
    }

    [Test]
    public void RepeatedNonDisposableCollection_ShouldRetainEveryEntry([Values] bool boxedValue)
    {
        var instance = boxedValue ? 42 : new object();
        var cleanedInstances = new List<object>();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(factory: (_, ref collector) =>
            {
                collector.Add(instance, cleanedInstances.Add);
                collector.Add(instance, cleanup: null);
                collector.Add(instance, _ => { });
                collector.Add(instance, cleanedInstances.Add);

                return new object();
            });
        });

        container.Resolve<object>();
        container.Dispose();

        Assert.That(cleanedInstances, Has.Count.EqualTo(2));
        Assert.That(cleanedInstances[0], Is.SameAs(instance));
        Assert.That(cleanedInstances[1], Is.SameAs(instance));
    }

    [Test]
    public void ReturnedAndCollectedInstance_ShouldCreateSeparateCleanupEntries(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var resource = new Resource();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: (_, ref collector) =>
            {
                collector.Add(resource);
                return resource;
            }, lifeTime);
        });

        container.Resolve<Resource>();

        Assert.That(resource.DisposeCount, Is.Zero);

        container.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(2));
    }

    [Test]
    public void FactoriesReturningSameReference_ShouldKeepIndependentCleanupPolicies(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var resource = new Resource();
        var customCleanupCount = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(tag: "owner", instance: resource);
            registrar.Register<object>(tag: "other", factory: () => resource, lifeTime,
                cleanup: _ => customCleanupCount++);
        });

        Assert.That(container.Resolve<Resource>("owner"), Is.SameAs(resource));
        Assert.That(container.Resolve<object>("other"), Is.SameAs(resource));
        Assert.That(resource.DisposeCount, Is.Zero);
        Assert.That(customCleanupCount, Is.Zero);

        container.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
        Assert.That(customCleanupCount, Is.EqualTo(1));
    }

    [Test]
    public void TransientFactoryReturningSameReference_ShouldRecordEachResolution([Values] bool customCleanup)
    {
        var resource = new Resource();
        var cleanupCount = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(factory: () => resource, cleanup: customCleanup ? _ => cleanupCount++ : null);
        });

        Assert.That(container.Resolve<Resource>(), Is.SameAs(resource));
        Assert.That(container.Resolve<Resource>(), Is.SameAs(resource));
        Assert.That(cleanupCount, Is.Zero);
        Assert.That(resource.DisposeCount, Is.Zero);

        container.Dispose();

        Assert.That(cleanupCount, Is.EqualTo(customCleanup ? 2 : 0));
        Assert.That(resource.DisposeCount, Is.EqualTo(customCleanup ? 0 : 2));
    }

    [Test]
    public void MultipleInterfaces_ShouldShareInstance_WithIndependentCleanupPolicies(
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var customCleanupCount = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Resource>(lifeTime);
            registrar.Register<IFirst>(
                factory: (resolver, ref _) => resolver.Resolve<Resource>(),
                lifeTime,
                cleanup: _ => customCleanupCount++
            );
            registrar.Register<ISecond>(
                factory: (resolver, ref _) => resolver.Resolve<Resource>(),
                lifeTime,
                cleanup: _ => { }
            );
        });
        var first = container.Resolve<IFirst>();
        var second = container.Resolve<ISecond>();
        var resource = container.Resolve<Resource>();

        Assert.That(first, Is.SameAs(resource));
        Assert.That(second, Is.SameAs(resource));
        Assert.That(container.Resolve<IFirst>(), Is.SameAs(resource));
        Assert.That(container.Resolve<ISecond>(), Is.SameAs(resource));

        container.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
        Assert.That(customCleanupCount, Is.EqualTo(1));
    }
}
