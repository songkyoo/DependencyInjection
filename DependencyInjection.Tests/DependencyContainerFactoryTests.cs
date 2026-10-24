namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerFactoryTests
{
    private sealed class Foo;

    private sealed class FooWithDefaultCtor
    {
        public string Name => "Default";
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
