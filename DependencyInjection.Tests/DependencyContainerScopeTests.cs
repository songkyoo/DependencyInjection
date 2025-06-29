namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerScopeTests
{
    private sealed class Foo;

    [Test]
    public void Scoped_ShouldCreateDifferentInstancesInChildContainer()
    {
        var builder = new DependencyContainerBuilder();
        builder.Register(Key.Of<Foo>(), factory: _ => new Foo(), LifeTime.Scoped);

        var root = builder.Build();
        var child = root.CreateScope();

        var foo1 = root.Resolve(Key.Of<Foo>());
        var foo2 = child.Resolve(Key.Of<Foo>());

        Assert.That(foo1, Is.Not.SameAs(foo2));
    }

    [Test]
    public void Singleton_ShouldBeSharedBetweenParentAndChild()
    {
        var builder = new DependencyContainerBuilder();
        builder.Register(Key.Of<Foo>(), factory: _ => new Foo(), LifeTime.Singleton);

        var root = builder.Build();
        var child = root.CreateScope();

        var foo1 = root.Resolve(Key.Of<Foo>());
        var foo2 = child.Resolve(Key.Of<Foo>());

        Assert.That(foo1, Is.SameAs(foo2));
    }

    [Test]
    public void ChildCanOverrideParentRegistration()
    {
        var key = Key.Of<Foo>();
        var rootBuilder = new DependencyContainerBuilder();
        rootBuilder.Register(key, factory: _ => new Foo(), LifeTime.Singleton);

        var root = rootBuilder.Build();

        var overridden = new Foo();
        var childBuilder = new DependencyContainerBuilder();
        childBuilder.Register(key, factory: _ => overridden, LifeTime.Scoped);

        var child = childBuilder.Build(root);

        var resolved = child.Resolve(key);

        Assert.That(resolved, Is.SameAs(overridden));
    }

    [Test]
    public void ParentCanBeResolvedFromChild()
    {
        var key = Key.Of<Foo>();
        var builder = new DependencyContainerBuilder();
        builder.Register(key, factory: _ => new Foo(), LifeTime.Transient);

        var root = builder.Build();
        var child = root.CreateScope();

        Assert.That(child.Resolve(key), Is.InstanceOf<Foo>());
    }
}
