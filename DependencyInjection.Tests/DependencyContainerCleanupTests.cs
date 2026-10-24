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
            registrar.Register<Resource>(lifetime, cleanup: _ => { });
        });
        var resource = container.Resolve<Resource>();

        container.Dispose();

        Assert.That(resource.DisposeCount, Is.Zero);

        resource.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void NonDisposableService_ShouldSupportDefaultAndCustomCleanup(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime,
        [Values(false, true)] bool customCleanup
    )
    {
        var key = new Key<IService>("service");
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<IService, Service>(
                key,
                lifetime,
                cleanup: customCleanup ? service => service.CleanupCount++ : null
            );
        });
        var service = container.Resolve(key);

        container.Dispose();

        Assert.That(service.CleanupCount, Is.EqualTo(customCleanup ? 1 : 0));
    }

    [Test]
    public void CoreRegistration_ShouldApplyCleanupPolicyToEachCreatedInstance(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime,
        [Values(false, true)] bool customCleanup
    )
    {
        var key = new Key<Resource>("resource");
        var cleanedInstances = new List<Resource>();
        Action<Resource>? cleanup = customCleanup ? cleanedInstances.Add : null;

        var container = DependencyContainerFactory.Create(registrar =>
        {
            switch (lifetime)
            {
                case Lifetime.Transient:
                    registrar.RegisterTransient(key, (_, ref _) => new Resource(), cleanup);
                    break;
                case Lifetime.Scoped:
                    registrar.RegisterScoped(key, (_, ref _) => new Resource(), cleanup);
                    break;
                case Lifetime.Singleton:
                    registrar.RegisterSingleton(key, (_, ref _) => new Resource(), cleanup);
                    break;
            }
        });
        var first = container.Resolve(key);
        var second = container.Resolve(key);

        Assert.That(second, lifetime == Lifetime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));
        Assert.That(cleanedInstances, Is.Empty);
        Assert.That(first.DisposeCount, Is.Zero);
        Assert.That(second.DisposeCount, Is.Zero);

        container.Dispose();
        container.Dispose();

        var createdInstances = lifetime == Lifetime.Transient ? new[] { first, second } : new[] { first };

        Assert.That(cleanedInstances, Is.EquivalentTo(customCleanup ? createdInstances : Array.Empty<Resource>()));

        foreach (var instance in createdInstances)
        {
            Assert.That(instance.DisposeCount, Is.EqualTo(customCleanup ? 0 : 1));
        }
    }

    [TestCaseSource(nameof(CustomCleanupRegistrations))]
    public void RegistrationOverload_ShouldApplyCustomCleanup(
        Action<IDependencyRegistrar, Action<IService>> register,
        Key<IService> key
    )
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            register(registrar, service => service.CleanupCount++);
        });
        var service = container.Resolve(key);

        container.Dispose();

        Assert.That(service.CleanupCount, Is.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> CustomCleanupRegistrations()
    {
        var key = new Key<IService>("service");

        yield return Registration("GeneralFactoryWithKey", key,
            (registrar, cleanup) => registrar.Register(key, () => new Service(), Lifetime.Scoped, cleanup));
        yield return Registration("GeneralContainerFactoryWithKey", key,
            (registrar, cleanup) => registrar.Register(key, (_, ref _) => new Service(), Lifetime.Scoped, cleanup));
        yield return Registration("ScopedContainerFactoryWithKey", key,
            (registrar, cleanup) => registrar.RegisterScoped(key, (_, ref _) => new Service(), cleanup));
        yield return Registration("SingletonContainerFactory", Key<IService>.Default,
            (registrar, cleanup) => registrar.RegisterSingleton((_, ref _) => new Service(), cleanup));
        yield return Registration("SingletonFactory", Key<IService>.Default,
            (registrar, cleanup) => registrar.RegisterSingleton(() => new Service(), cleanup));
        yield return Registration("ScopedImplementationWithKey", key,
            (registrar, cleanup) => registrar.RegisterScoped<IService, Service>(key, cleanup));
        yield return Registration("SingletonImplementation", Key<IService>.Default,
            (registrar, cleanup) => registrar.RegisterSingleton<IService, Service>(cleanup));
        yield return Registration("GeneralImplementation", Key<IService>.Default,
            (registrar, cleanup) => registrar.Register<IService, Service>(Lifetime.Scoped, cleanup));
        yield return Registration("GeneralImplementationWithKey", key,
            (registrar, cleanup) => registrar.Register<IService, Service>(key, Lifetime.Singleton, cleanup));
        yield return Registration("InstanceWithKey", key,
            (registrar, cleanup) => registrar.RegisterSingleton(key, new Service(), cleanup));
        yield return Registration("Instance", Key<IService>.Default,
            (registrar, cleanup) => registrar.RegisterSingleton(new Service(), cleanup));
    }

    private static TestCaseData Registration(
        string name,
        Key<IService> key,
        Action<IDependencyRegistrar, Action<IService>> register
    )
    {
        return new TestCaseData(register, key).SetName($"CustomCleanup_{name}");
    }
}
