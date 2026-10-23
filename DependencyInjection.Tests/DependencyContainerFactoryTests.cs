namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerFactoryTests
{
    private sealed class Foo;

    private sealed class FooWithDefaultCtor
    {
        public string Name { get; set; } = "Default";
    }

    [Test]
    public void Register_SameKeyTwice_ShouldThrowArgumentException()
    {
        var key = Key<Foo>.Default;

        Assert.That(() => DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, factory: (_, ref _) => new Foo(), Lifetime.Transient);
            registrar.Register(key, factory: (_, ref _) => new Foo(), Lifetime.Singleton);
        }), Throws.ArgumentException);
    }

    [Test]
    public void Register_GenericFactory_ShouldResolveCorrectly()
    {
        var key = Key<Foo>.Default;
        var configurationCalls = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            configurationCalls++;
            registrar.Register(key, factory: (_, ref _) => new Foo(), Lifetime.Scoped);
        });

        Assert.That(configurationCalls, Is.EqualTo(1));
        Assert.That(container.Resolve(key), Is.InstanceOf<Foo>());
    }

    [Test]
    public void Register_DefaultConstructor_ShouldResolveInstance()
    {
        var key = Key<FooWithDefaultCtor>.Default;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, Lifetime.Singleton);
        });
        var instance = container.Resolve(key);

        Assert.That(instance.Name, Is.EqualTo("Default"));
    }

    [Test]
    public void ExplicitEmptyConfiguration_ShouldReturnUsableEmptyContainer()
    {
        using var container = DependencyContainerFactory.Create(_ => { });

        Assert.That(container.Contains<Foo>(), Is.False);

        using var child = container.CreateScope(registrar => registrar.RegisterScoped<Foo>());

        Assert.That(child.Resolve<Foo>(), Is.Not.Null);
        Assert.That(container.Contains<Foo>(), Is.False);
    }

    [Test]
    public void ConfigurationFailure_ShouldPropagateOriginalException()
    {
        var failure = new ArgumentException("Configuration failed.");

        var exception = Assert.Throws<ArgumentException>(() => DependencyContainerFactory.Create(_ => throw failure));

        Assert.That(exception, Is.SameAs(failure));
    }

    [Test]
    public void ReusedConfiguration_ShouldCreateIndependentRoots(
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        void Configure(IDependencyRegistrar registrar)
        {
            registrar.Register(factory: () => new Foo(), lifetime);
        }

        using var first = DependencyContainerFactory.Create(Configure);
        using var second = DependencyContainerFactory.Create(Configure);
        var firstInstance = first.Resolve<Foo>();

        Assert.That(first.Resolve<Foo>(), Is.SameAs(firstInstance));
        Assert.That(second.Resolve<Foo>(), Is.Not.SameAs(firstInstance));
    }

    [Test]
    public void PublicApi_ShouldExposeFactory_WithoutContainerImplementationOrBuilder()
    {
        var exportedTypeNames = typeof(DependencyContainerFactory).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .ToArray();

        Assert.That(exportedTypeNames, Does.Contain("Macaron.DependencyInjection.DependencyContainerFactory"));
        Assert.That(exportedTypeNames, Does.Not.Contain("Macaron.DependencyInjection.DependencyContainer"));
        Assert.That(exportedTypeNames, Does.Not.Contain("Macaron.DependencyInjection.DependencyContainerBuilder"));
    }
}
