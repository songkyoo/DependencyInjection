namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerScopeTests
{
    private sealed class Foo;

    [Test]
    public void Scoped_ShouldCreateDifferentInstancesInChildContainer()
    {
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(Key.Of<Foo>(), factory: (_, ref _) => new Foo(), LifeTime.Scoped);
        });
        var child = root.CreateScope();
        var foo1 = root.Resolve(Key.Of<Foo>());
        var foo2 = child.Resolve(Key.Of<Foo>());

        Assert.That(foo1, Is.Not.SameAs(foo2));
    }

    [Test]
    public void Singleton_ShouldBeSharedBetweenParentAndChild()
    {
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(Key.Of<Foo>(), factory: (_, ref _) => new Foo(), LifeTime.Singleton);
        });
        var child = root.CreateScope();
        var foo1 = root.Resolve(Key.Of<Foo>());
        var foo2 = child.Resolve(Key.Of<Foo>());

        Assert.That(foo1, Is.SameAs(foo2));
    }

    [Test]
    public void ChildCanOverrideParentRegistration()
    {
        var key = Key.Of<Foo>();
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, factory: (_, ref _) => new Foo(), LifeTime.Singleton);
        });
        var overridden = new Foo();
        var child = root.CreateScope(registrar =>
        {
            registrar.Register(key, factory: (_, ref _) => overridden, LifeTime.Scoped);
        });
        var resolved = child.Resolve(key);

        Assert.That(resolved, Is.SameAs(overridden));
    }

    [Test]
    public void ParentCanBeResolvedFromChild()
    {
        var key = Key.Of<Foo>();
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, factory: (_, ref _) => new Foo(), LifeTime.Transient);
        });
        var child = root.CreateScope();

        Assert.That(child.Resolve(key), Is.InstanceOf<Foo>());
    }
}
