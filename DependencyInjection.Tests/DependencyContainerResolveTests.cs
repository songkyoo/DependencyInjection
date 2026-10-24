// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerResolveTests
{
    private sealed class Foo;

    [Test]
    public void DefaultTagExtensions_ShouldUseDefaultTag([Values] bool registerExplicitTag)
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            if (registerExplicitTag)
            {
                registrar.RegisterSingleton<Foo>(factory: () => new Foo(), tag: null);
            }
            else
            {
                registrar.RegisterSingleton(factory: () => new Foo());
            }
        });
        var service = container.Resolve<Foo>(null);

        Assert.That(container.Contains<Foo>(), Is.True);
        Assert.That(container.Resolve<Foo>(), Is.SameAs(service));
        Assert.That(container.ResolveOrNull<Foo>(), Is.SameAs(service));
        Assert.That(container.ResolveOrElse<Foo>(_ => throw new InvalidOperationException()), Is.SameAs(service));
        Assert.That(container.ResolveOrElse<Foo>(() => throw new InvalidOperationException()), Is.SameAs(service));
        Assert.That(container.ResolveOrElse(defaultValue: new Foo()), Is.SameAs(service));
    }

    [TestCase("")]
    [TestCase("service")]
    public void MissingTag_ShouldNotFallBackToDefaultOrAnotherTag(string name)
    {
        var registeredTag = name;
        var missingTag = "missing";
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Foo>();
            registrar.RegisterSingleton<Foo>(tag: registeredTag);
        });
        var fallback = new Foo();
        var factoryCalls = 0;

        Assert.That(container.Contains<Foo>(missingTag), Is.False);
        Assert.That(() => container.Resolve<Foo>(missingTag), Throws.InvalidOperationException);
        Assert.That(container.ResolveOrNull<Foo>(missingTag), Is.Null);
        Assert.That(container.ResolveOrElse<Foo>(fallback, tag: missingTag), Is.SameAs(fallback));
        Assert.That(
            container.ResolveOrElse<Foo>(() =>
            {
                factoryCalls++;

                return fallback;
            }, tag: missingTag),
            Is.SameAs(fallback)
        );
        Assert.That(
            container.ResolveOrElse<Foo>(resolver =>
            {
                Assert.That(resolver, Is.SameAs(container));

                factoryCalls++;

                return fallback;
            }, tag: missingTag),
            Is.SameAs(fallback)
        );
        Assert.That(factoryCalls, Is.EqualTo(2));
    }

    [Test]
    public void NamedRegistration_ShouldNotProvideDefaultService()
    {
        var tag = "";
        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton<Foo>(tag: tag));
        var fallback = new Foo();

        Assert.That(container.Contains<Foo>(), Is.False);
        Assert.That(() => container.Resolve<Foo>(), Throws.InvalidOperationException);
        Assert.That(container.ResolveOrNull<Foo>(), Is.Null);
        Assert.That(container.ResolveOrElse(fallback), Is.SameAs(fallback));
        Assert.That(container.ResolveOrElse(() => fallback), Is.SameAs(fallback));
        Assert.That(container.ResolveOrElse(_ => fallback), Is.SameAs(fallback));
    }

    [Test]
    public void ResolveOrNull_ValueType_ShouldUseExactTagAndDefaultTag()
    {
        var tag = "answer";
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<int>(instance: 42, tag: tag);
            registrar.RegisterSingleton(instance: 7);
        });

        Assert.That(container.ResolveOrNull<int>(tag), Is.EqualTo(42));
        Assert.That(container.ResolveOrNull<int>("missing"), Is.Null);
        Assert.That(container.ResolveOrNull<int>(), Is.EqualTo(7));
        Assert.That(container.ResolveOrNull<long>(), Is.Null);
    }

    [Test]
    public void UnregisteredTag_ShouldThrowWithTypeAndTag()
    {
        var tag = "missing";
        using var container = DependencyContainerFactory.Create(_ => { });

        var error = Assert.Throws<InvalidOperationException>(() => container.Resolve<Foo>(tag));

        Assert.That(error!.Message, Does.Contain(typeof(Foo).ToString()));
        Assert.That(error.Message, Does.Contain(tag));
    }
}
