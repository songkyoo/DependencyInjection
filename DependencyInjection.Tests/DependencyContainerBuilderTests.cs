namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerBuilderTests
{
    private sealed class Foo;

    private sealed class FooWithDefaultCtor
    {
        public string Name { get; set; } = "Default";
    }

    [Test]
    public void Register_SameKeyTwice_ShouldThrowArgumentException()
    {
        var builder = new DependencyContainerBuilder();
        var key = Key.Of<Foo>();

        builder.Register(key, factory: _ => new Foo(), LifeTime.Transient);

        Assert.That(() => builder.Register(key, factory: _ => new Foo(), LifeTime.Singleton), Throws.ArgumentException);
    }

    [Test]
    public void Register_GenericFactory_ShouldResolveCorrectly()
    {
        var key = Key.Of<Foo>();
        var builder = new DependencyContainerBuilder();
        builder.Register(key, factory: _ => new Foo(), LifeTime.Scoped);

        var container = builder.Build();

        Assert.That(container.Resolve(key), Is.InstanceOf<Foo>());
    }

    [Test]
    public void Register_DefaultConstructor_ShouldResolveInstance()
    {
        var key = Key.Of<FooWithDefaultCtor>();
        var builder = new DependencyContainerBuilder();
        builder.Register(key, LifeTime.Singleton);

        var container = builder.Build();
        var instance = container.Resolve(key);

        Assert.That(instance.Name, Is.EqualTo("Default"));
    }
}
