namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerFactoryTests
{
    private sealed class Foo;

    private sealed class FooWithDefaultCtor
    {
        public string Name => "Default";
    }

    [TestCase(null)]
    [TestCase("service")]
    public void NullInstance_ShouldFailDuringRegistration_AndLeaveTheRegistrationAvailable(string? tag)
    {
        var instance = new Foo();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            var error = Assert.Throws<ArgumentNullException>(() =>
                registrar.RegisterSingleton<Foo>(instance: null!, tag));

            Assert.That(error!.ParamName, Is.EqualTo("instance"));
            registrar.RegisterSingleton(instance, tag);
        });

        Assert.That(container.Resolve<Foo>(tag), Is.SameAs(instance));
    }

    [TestCase(0)]
    [TestCase(42)]
    public void ValueTypeInstance_ShouldRegisterAndResolve(int instance)
    {
        using var container = DependencyContainerFactory.Create(registrar =>
            registrar.RegisterSingleton(instance: instance));

        Assert.That(container.Resolve<int>(), Is.EqualTo(instance));
    }

    [Test]
    public void Register_ExplicitFactory_ShouldResolveInstance()
    {
        object? tag = null;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<FooWithDefaultCtor>(factory: () => new FooWithDefaultCtor(), Lifetime.Singleton, tag: tag);
        });
        var instance = container.Resolve<FooWithDefaultCtor>(tag);

        Assert.That(instance.Name, Is.EqualTo("Default"));
    }

    [Test]
    public void ExplicitEmptyConfiguration_ShouldReturnUsableEmptyContainer()
    {
        using var container = DependencyContainerFactory.Create(_ => { });

        Assert.That(container.Contains<Foo>(), Is.False);

        using var child = container.CreateChild(registrar => registrar.RegisterScoped<Foo>(factory: () => new Foo()));

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
