namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerResolveTests
{
    private interface IFoo;

    private sealed class Foo : IFoo;

    private sealed record NamedTag(string Value);

    [Test]
    public void Transient_ShouldCreateNewInstanceEachTime()
    {
        var builder = new DependencyContainerBuilder();

        builder.Register(key: Key.Of<IFoo>(), factory: (_, ref _) => new Foo(), LifeTime.Transient);

        var container = builder.Build();
        var foo1 = container.Resolve(Key.Of<IFoo>());
        var foo2 = container.Resolve(Key.Of<IFoo>());

        Assert.That(foo1, Is.Not.SameAs(foo2));
    }

    [Test]
    public void Scoped_ShouldReuseInstanceInSameContainer()
    {
        var builder = new DependencyContainerBuilder();

        builder.Register(Key.Of<Foo>(), factory: (_, ref _) => new Foo(), LifeTime.Scoped);

        var container = builder.Build();
        var foo1 = container.Resolve(Key.Of<Foo>());
        var foo2 = container.Resolve(Key.Of<Foo>());

        Assert.That(foo1, Is.SameAs(foo2));
    }

    [Test]
    public void Singleton_ShouldReuseInstanceAcrossContainers()
    {
        var builder = new DependencyContainerBuilder();

        builder.Register(Key.Of<Foo>(), factory: (_, ref _) => new Foo(), LifeTime.Singleton);

        var root = builder.Build();
        var child = root.CreateScope();
        var rootFoo = root.Resolve(Key.Of<Foo>());
        var childFoo = child.Resolve(Key.Of<Foo>());

        Assert.That(rootFoo, Is.SameAs(childFoo));
    }

    [Test]
    public void Resolve_WithObjectTag_ShouldUseTagValueEquality()
    {
        var builder = new DependencyContainerBuilder();

        builder.Register(Key.Of<Foo>(new NamedTag("primary")), factory: (_, ref _) => new Foo(), LifeTime.Singleton);

        var container = builder.Build();

        Assert.That(container.Resolve(Key.Of<Foo>(new NamedTag("primary"))), Is.InstanceOf<Foo>());
        Assert.That(container.Contains(Key.Of<Foo>(new NamedTag("primary"))), Is.True);
    }

    [Test]
    public void Resolve_WithDifferentObjectTag_ShouldNotMatch()
    {
        var builder = new DependencyContainerBuilder();

        builder.Register(Key.Of<Foo>(new NamedTag("primary")), factory: (_, ref _) => new Foo(), LifeTime.Singleton);

        var container = builder.Build();

        Assert.That(container.Contains(Key.Of<Foo>(new NamedTag("secondary"))), Is.False);
    }

    [Test]
    public void Resolve_TypeBasedUntaggedExtensions_ShouldUseNullTag()
    {
        var builder = new DependencyContainerBuilder();

        builder.Register(Key.Of<Foo>(), factory: (_, ref _) => new Foo(), LifeTime.Singleton);

        var container = builder.Build();

        Assert.That(container.Contains(typeof(Foo)), Is.True);
        Assert.That(container.Resolve(typeof(Foo)), Is.InstanceOf<Foo>());
        Assert.That(container.ResolveOrNull(typeof(Foo)), Is.InstanceOf<Foo>());
        Assert.That(container.ResolveOrElse(typeof(Foo), _ => throw new InvalidOperationException()), Is.InstanceOf<Foo>());
        Assert.That(container.ResolveOrElse(typeof(Foo), () => throw new InvalidOperationException()), Is.InstanceOf<Foo>());
        Assert.That(container.ResolveOrElse(type: typeof(Foo), defaultValue: new Foo()), Is.InstanceOf<Foo>());
    }

    [Test]
    public void ResolveOrNull_ValueTypeWithTag_ShouldReturnRegisteredValue()
    {
        var builder = new DependencyContainerBuilder();

        builder.Register(Key.Of<int>("answer"), factory: (_, ref _) => 42, LifeTime.Singleton);

        var container = builder.Build();

        Assert.That(container.ResolveOrNull<int>("answer"), Is.EqualTo(42));
        Assert.That(container.ResolveOrNull<int>("missing"), Is.Null);
    }

    [Test]
    public void UnregisteredKey_ShouldThrow()
    {
        var builder = new DependencyContainerBuilder();
        var container = builder.Build();

        Assert.That(() => container.Resolve(Key.Of<Foo>()), Throws.InvalidOperationException);
    }
}
