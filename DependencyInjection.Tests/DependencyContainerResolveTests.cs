namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerResolveTests
{
    private interface IFoo;

    private sealed class Foo : IFoo;

    [Test]
    public void Transient_ShouldCreateNewInstanceEachTime()
    {
        var builder = new DependencyContainerBuilder();
        builder.Register(Key.Of<IFoo>(), factory: _ => new Foo(), LifeTime.Transient);

        var container = builder.Build();
        var foo1 = container.Resolve(Key.Of<IFoo>());
        var foo2 = container.Resolve(Key.Of<IFoo>());

        Assert.That(foo1, Is.Not.SameAs(foo2));
    }

    [Test]
    public void Scoped_ShouldReuseInstanceInSameContainer()
    {
        var builder = new DependencyContainerBuilder();
        builder.Register(Key.Of<Foo>(), factory: _ => new Foo(), LifeTime.Scoped);

        var container = builder.Build();
        var foo1 = container.Resolve(Key.Of<Foo>());
        var foo2 = container.Resolve(Key.Of<Foo>());

        Assert.That(foo1, Is.SameAs(foo2));
    }

    [Test]
    public void Singleton_ShouldReuseInstanceAcrossContainers()
    {
        var builder = new DependencyContainerBuilder();
        builder.Register(Key.Of<Foo>(), factory: _ => new Foo(), LifeTime.Singleton);

        var root = builder.Build();
        var child = root.CreateScope();

        var rootFoo = root.Resolve(Key.Of<Foo>());
        var childFoo = child.Resolve(Key.Of<Foo>());

        Assert.That(rootFoo, Is.SameAs(childFoo));
    }

    [Test]
    public void UnregisteredKey_ShouldThrow()
    {
        var builder = new DependencyContainerBuilder();
        var container = builder.Build();

        Assert.That(() => container.Resolve(Key.Of<Foo>()), Throws.InvalidOperationException);
    }
}
