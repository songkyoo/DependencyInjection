// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerKeyTests
{
    private sealed class Service;

    [Test]
    public void SameKeyRegisteredTwice_ShouldFailBeforeFactoriesRun(
        [Values] Lifetime firstLifetime,
        [Values] Lifetime secondLifetime,
        [Values] bool useDefaultKey
    )
    {
        var key = useDefaultKey ? Key<Service>.Default : new Key<Service>("service");
        var factoryCalls = 0;

        var error = Assert.Throws<ArgumentException>(() => DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, CreateService, firstLifetime);
            registrar.Register(key, CreateService, secondLifetime);
        }));

        Assert.That(error!.Message, Does.Contain(key.ToString()));
        Assert.That(factoryCalls, Is.Zero);

        Service CreateService()
        {
            factoryCalls++;
            return new Service();
        }
    }

    [Test]
    public void DefaultRegistration_ShouldConflictWithExplicitDefaultKey([Values] Lifetime lifetime)
    {
        Assert.That(() => DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Service>(lifetime);
            registrar.Register(Key<Service>.Default, lifetime);
        }), Throws.ArgumentException);
    }

    [Test]
    public void SameNameKeys_ShouldKeepIndependentRegistrationsAndCaches([Values] Lifetime lifetime)
    {
        var firstKey = new Key<Service>("service");
        var secondKey = new Key<Service>("service");
        var firstCreations = 0;
        var secondCreations = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(firstKey, () => { firstCreations++; return new Service(); }, lifetime);
            registrar.Register(secondKey, () => { secondCreations++; return new Service(); }, lifetime);
        });
        var first = container.Resolve(firstKey);
        var second = container.Resolve(secondKey);

        Assert.That(container.Contains(firstKey), Is.True);
        Assert.That(container.Contains(secondKey), Is.True);
        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(container.Resolve(firstKey), lifetime == Lifetime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));
        Assert.That(container.Resolve(secondKey), lifetime == Lifetime.Transient ? Is.Not.SameAs(second) : Is.SameAs(second));
        Assert.That(firstCreations, Is.EqualTo(lifetime == Lifetime.Transient ? 2 : 1));
        Assert.That(secondCreations, Is.EqualTo(lifetime == Lifetime.Transient ? 2 : 1));
    }

    [Test]
    public void SameNameChildKey_ShouldNotMaskParentRegistrationOrInitialization(
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime parentLifetime,
        [Values] Lifetime childLifetime
    )
    {
        var parentKey = new Key<Service>("service");
        var childKey = new Key<Service>("service");
        var parentCreations = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(parentKey, () => { parentCreations++; return new Service(); }, parentLifetime);
        });
        using var child = root.CreateScope(registrar => registrar.Register(childKey, childLifetime));

        Assert.That(parentCreations, Is.EqualTo(parentLifetime == Lifetime.Scoped ? 2 : 1));
        Assert.That(child.Contains(parentKey), Is.True);
        Assert.That(child.Contains(childKey), Is.True);
        Assert.That(root.Contains(childKey), Is.False);
        Assert.That(child.Resolve(parentKey), Is.Not.SameAs(child.Resolve(childKey)));
        Assert.That(child.Resolve(parentKey), parentLifetime == Lifetime.Singleton
            ? Is.SameAs(root.Resolve(parentKey))
            : Is.Not.SameAs(root.Resolve(parentKey)));
        Assert.That(child.Contains(new Key<Service>("service")), Is.False);
    }

    [Test]
    public void SameNameDependencyKeys_ShouldNotBeDetectedAsCycle()
    {
        var firstKey = new Key<Service>("service");
        var secondKey = new Key<Service>("service");
        var secondCreations = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(firstKey, factory: (resolver, ref _) =>
            {
                resolver.Resolve(secondKey);
                return new Service();
            });
            registrar.RegisterTransient(secondKey, factory: () =>
            {
                secondCreations++;
                return new Service();
            });
        });

        Assert.That(() => container.Resolve(firstKey), Throws.Nothing);
        Assert.That(secondCreations, Is.EqualTo(1));
    }

    [Test]
    public void RepeatedKeyReference_ShouldBeDetectedAsCycleEvenWithSameNames()
    {
        var firstKey = new Key<Service>("service");
        var secondKey = new Key<Service>("service");
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(firstKey, factory: (resolver, ref _) => resolver.Resolve(secondKey));
            registrar.RegisterTransient(secondKey, factory: (resolver, ref _) => resolver.Resolve(firstKey));
        });

        var error = Assert.Throws<InvalidOperationException>(() => container.Resolve(firstKey));

        Assert.That(error!.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(error.Message, Does.Contain(firstKey.ToString()));
    }

    [Test]
    public void SharedKeyAcrossIndependentContainers_ShouldNotShareInstances()
    {
        var key = new Key<Service>("service");
        using var first = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(key));
        using var second = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(key));

        Assert.That(first.Resolve(key), Is.Not.SameAs(second.Resolve(key)));
    }
}
