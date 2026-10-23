namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerResolveTests
{
    private interface IFoo;

    private sealed class Foo : IFoo;

    [Test]
    public void Transient_ShouldCreateNewInstanceEachTime()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key: Key<IFoo>.Default, factory: (_, ref _) => new Foo(), LifeTime.Transient);
        });

        var first = container.Resolve(Key<IFoo>.Default);
        var second = container.Resolve(Key<IFoo>.Default);

        Assert.That(first, Is.Not.SameAs(second));
    }

    [Test]
    public void DefaultKeyExtensions_ShouldUseDefaultKey([Values] bool registerExplicitKey)
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            if (registerExplicitKey)
            {
                registrar.RegisterSingleton(Key<Foo>.Default, factory: () => new Foo());
            }
            else
            {
                registrar.RegisterSingleton(factory: () => new Foo());
            }
        });
        var service = container.Resolve(Key<Foo>.Default);

        Assert.That(container.Contains<Foo>(), Is.True);
        Assert.That(container.Resolve<Foo>(), Is.SameAs(service));
        Assert.That(container.ResolveOrNull<Foo>(), Is.SameAs(service));
        Assert.That(container.ResolveOrElse<Foo>(_ => throw new InvalidOperationException()), Is.SameAs(service));
        Assert.That(container.ResolveOrElse<Foo>(() => throw new InvalidOperationException()), Is.SameAs(service));
        Assert.That(container.ResolveOrElse(defaultValue: new Foo()), Is.SameAs(service));
    }

    [TestCase("")]
    [TestCase("service")]
    public void MissingKey_ShouldNotMatchDefaultOrSameName(string name)
    {
        var registeredKey = new Key<Foo>(name);
        var missingKey = new Key<Foo>(name);
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Foo>();
            registrar.RegisterSingleton<Foo>(registeredKey);
        });
        var fallback = new Foo();
        var factoryCalls = 0;

        Assert.That(container.Contains(missingKey), Is.False);
        Assert.That(() => container.Resolve(missingKey), Throws.InvalidOperationException);
        Assert.That(container.ResolveOrNull(missingKey), Is.Null);
        Assert.That(container.ResolveOrElse(missingKey, fallback), Is.SameAs(fallback));
        Assert.That(container.ResolveOrElse(missingKey, () =>
        {
            factoryCalls++;
            return fallback;
        }), Is.SameAs(fallback));
        Assert.That(container.ResolveOrElse(missingKey, resolver =>
        {
            Assert.That(resolver, Is.SameAs(container));
            factoryCalls++;
            return fallback;
        }), Is.SameAs(fallback));
        Assert.That(factoryCalls, Is.EqualTo(2));
    }

    [Test]
    public void NamedRegistration_ShouldNotProvideDefaultService()
    {
        var key = new Key<Foo>();
        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton<Foo>(key));
        var fallback = new Foo();

        Assert.That(container.Contains<Foo>(), Is.False);
        Assert.That(container.Resolve<Foo>, Throws.InvalidOperationException);
        Assert.That(container.ResolveOrNull<Foo>(), Is.Null);
        Assert.That(container.ResolveOrElse(fallback), Is.SameAs(fallback));
        Assert.That(container.ResolveOrElse(() => fallback), Is.SameAs(fallback));
        Assert.That(container.ResolveOrElse(_ => fallback), Is.SameAs(fallback));
    }

    [Test]
    public void ResolveOrNull_ValueType_ShouldUseExactKeyAndDefaultKey()
    {
        var key = new Key<int>("answer");
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(key, instance: 42);
            registrar.RegisterSingleton(instance: 7);
        });

        Assert.That(container.ResolveOrNull(key), Is.EqualTo(42));
        Assert.That(container.ResolveOrNull(new Key<int>("answer")), Is.Null);
        Assert.That(container.ResolveOrNull<int>(), Is.EqualTo(7));
        Assert.That(container.ResolveOrNull<long>(), Is.Null);
    }

    [Test]
    public void UnregisteredKey_ShouldThrowWithTypeAndName()
    {
        var key = new Key<Foo>("missing");
        using var container = DependencyContainerFactory.Create();

        var error = Assert.Throws<InvalidOperationException>(() => container.Resolve(key));

        Assert.That(error!.Message, Does.Contain(key.ToString()));
    }
}
