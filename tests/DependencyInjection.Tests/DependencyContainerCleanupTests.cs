namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerCleanupTests
{
    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    public interface IService
    {
        int CleanupCount { get; set; }
    }

    private sealed class Service : IService
    {
        public int CleanupCount { get; set; }
    }

    [Test]
    public void EmptyCleanup_ShouldLeaveDisposalToCaller(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: () => new Resource(), lifetime, cleanup: _ => { });
        });
        var resource = container.Resolve<Resource>();

        container.Dispose();

        Assert.That(resource.DisposeCount, Is.Zero);

        resource.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void NonDisposableService_WithoutCleanup_ShouldRequireNoCleanup(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var tag = "service";
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<IService>(factory: () => new Service(), lifetime, cleanup: null, tag: tag);
        });
        var service = container.Resolve<IService>(tag);

        container.Dispose();

        Assert.That(service.CleanupCount, Is.Zero);
    }

    [Test]
    public void NonDisposableService_WithCustomCleanup_ShouldInvokeCallback(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var tag = "service";
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<IService>(
                factory: () => new Service(),
                lifetime,
                cleanup: service => service.CleanupCount++,
                tag: tag
            );
        });
        var service = container.Resolve<IService>(tag);

        container.Dispose();

        Assert.That(service.CleanupCount, Is.EqualTo(1));
    }

    [Test]
    public void CoreRegistration_WithoutCleanup_ShouldDisposeEachCreatedInstance(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var tag = "resource";
        var container = DependencyContainerFactory.Create(registrar =>
        {
            switch (lifetime)
            {
                case Lifetime.Transient:
                    registrar.RegisterTransient<Resource>((_, ref _) => new Resource(), cleanup: null, tag: tag);
                    break;
                case Lifetime.Scoped:
                    registrar.RegisterScoped<Resource>((_, ref _) => new Resource(), cleanup: null, tag: tag);
                    break;
                case Lifetime.Singleton:
                    registrar.RegisterSingleton<Resource>((_, ref _) => new Resource(), cleanup: null, tag: tag);
                    break;
            }
        });
        var first = container.Resolve<Resource>(tag);
        var second = container.Resolve<Resource>(tag);

        Assert.That(second, lifetime == Lifetime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));
        Assert.That(first.DisposeCount, Is.Zero);
        Assert.That(second.DisposeCount, Is.Zero);

        container.Dispose();
        container.Dispose();

        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void CoreRegistration_WithCustomCleanup_ShouldCleanEachCreatedInstance(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var tag = "resource";
        var cleanedInstances = new List<Resource>();
        Action<Resource> cleanup = cleanedInstances.Add;

        var container = DependencyContainerFactory.Create(registrar =>
        {
            switch (lifetime)
            {
                case Lifetime.Transient:
                    registrar.RegisterTransient<Resource>((_, ref _) => new Resource(), cleanup: cleanup, tag: tag);
                    break;
                case Lifetime.Scoped:
                    registrar.RegisterScoped<Resource>((_, ref _) => new Resource(), cleanup: cleanup, tag: tag);
                    break;
                case Lifetime.Singleton:
                    registrar.RegisterSingleton<Resource>((_, ref _) => new Resource(), cleanup: cleanup, tag: tag);
                    break;
            }
        });
        var first = container.Resolve<Resource>(tag);
        var second = container.Resolve<Resource>(tag);

        Assert.That(second, lifetime == Lifetime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));
        Assert.That(cleanedInstances, Is.Empty);
        Assert.That(first.DisposeCount, Is.Zero);
        Assert.That(second.DisposeCount, Is.Zero);

        container.Dispose();
        container.Dispose();

        var createdInstances = lifetime == Lifetime.Transient ? new[] { first, second } : new[] { first };

        Assert.That(cleanedInstances, Is.EquivalentTo(createdInstances));

        foreach (var instance in createdInstances)
        {
            Assert.That(instance.DisposeCount, Is.Zero);
        }
    }

    [TestCaseSource(nameof(CustomCleanupRegistrations))]
    public void RegistrationOverload_ShouldApplyCustomCleanup(
        Action<IDependencyRegistrar, Action<IService>> register,
        object? tag
    )
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            register(registrar, service => service.CleanupCount++);
        });
        var service = container.Resolve<IService>(tag);

        container.Dispose();

        Assert.That(service.CleanupCount, Is.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> CustomCleanupRegistrations()
    {
        var tag = "service";

        yield return Registration("GeneralFactoryWithTag", tag,
            (registrar, cleanup) => registrar.Register(() => new Service(), Lifetime.Scoped, cleanup: cleanup, tag: tag));
        yield return Registration("GeneralContainerFactoryWithTag", tag,
            (registrar, cleanup) => registrar.Register((_, ref _) => new Service(), Lifetime.Scoped, cleanup: cleanup, tag: tag));
        yield return Registration("ScopedContainerFactoryWithTag", tag,
            (registrar, cleanup) => registrar.RegisterScoped((_, ref _) => new Service(), cleanup: cleanup, tag: tag));
        yield return Registration("SingletonContainerFactory", null,
            (registrar, cleanup) => registrar.RegisterSingleton((_, ref _) => new Service(), cleanup: cleanup));
        yield return Registration("SingletonFactory", null,
            (registrar, cleanup) => registrar.RegisterSingleton(() => new Service(), cleanup: cleanup));
        yield return Registration("InstanceWithTag", tag,
            (registrar, cleanup) => registrar.RegisterSingleton(new Service(), cleanup: cleanup, tag: tag));
        yield return Registration("Instance", null,
            (registrar, cleanup) => registrar.RegisterSingleton(new Service(), cleanup: cleanup));
    }

    private static TestCaseData Registration(
        string name,
        object? tag,
        Action<IDependencyRegistrar, Action<IService>> register
    )
    {
        return new TestCaseData(register, tag).SetName($"CustomCleanup_{name}");
    }
}
