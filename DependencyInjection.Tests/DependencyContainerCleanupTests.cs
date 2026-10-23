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
    public void OmittedCleanup_ShouldDisposeCachedInstanceOnce(
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: () => new Resource(), lifeTime);
        });
        var resource = container.Resolve<Resource>();

        Assert.That(container.Resolve<Resource>(), Is.SameAs(resource));
        Assert.That(resource.DisposeCount, Is.Zero);

        container.Dispose();
        container.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void CustomCleanup_ShouldReplaceDispose_AndReceiveCachedInstanceOnce(
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var cleanedInstances = new List<Resource>();
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: (_, ref _) => new Resource(), lifeTime, cleanup: cleanedInstances.Add);
        });
        var resource = container.Resolve<Resource>();

        Assert.That(container.Resolve<Resource>(), Is.SameAs(resource));
        Assert.That(cleanedInstances, Is.Empty);

        container.Dispose();
        container.Dispose();

        Assert.That(cleanedInstances, Is.EqualTo(new[] { resource }));
        Assert.That(resource.DisposeCount, Is.Zero);
    }

    [Test]
    public void EmptyCleanup_ShouldLeaveDisposalToCaller(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Resource>(lifeTime, cleanup: _ => { });
        });
        var resource = container.Resolve<Resource>();

        container.Dispose();

        Assert.That(resource.DisposeCount, Is.Zero);

        resource.Dispose();

        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void NonDisposableService_ShouldSupportDefaultAndCustomCleanup(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values(false, true)] bool customCleanup
    )
    {
        var key = new Key<IService>("service");
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<IService, Service>(
                key,
                lifeTime,
                cleanup: customCleanup ? service => service.CleanupCount++ : null
            );
        });
        var service = container.Resolve(key);

        container.Dispose();

        Assert.That(service.CleanupCount, Is.EqualTo(customCleanup ? 1 : 0));
    }

    [Test]
    public void InheritedService_ShouldRunCleanupInItsOwningContainer(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Service>(lifeTime, cleanup: service => service.CleanupCount++);
        });
        var child = root.CreateScope();
        var rootService = root.Resolve<Service>();
        var childService = child.Resolve<Service>();

        child.Dispose();

        if (lifeTime != LifeTime.Singleton)
        {
            Assert.That(childService, Is.Not.SameAs(rootService));
            Assert.That(childService.CleanupCount, Is.EqualTo(1));
        }
        else
        {
            Assert.That(childService, Is.SameAs(rootService));
        }

        Assert.That(rootService.CleanupCount, Is.Zero);

        root.Dispose();

        Assert.That(rootService.CleanupCount, Is.EqualTo(1));
        Assert.That(childService.CleanupCount, Is.EqualTo(1));
    }

    [Test]
    public void CoreRegistration_ShouldApplyCleanupPolicy(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values(false, true)] bool customCleanup
    )
    {
        var key = new Key<Resource>("resource");
        var cleanupCount = 0;
        Action<Resource>? cleanup = customCleanup ? _ => cleanupCount++ : null;

        var container = DependencyContainerFactory.Create(registrar =>
        {
            switch (lifeTime)
            {
                case LifeTime.Transient:
                    registrar.RegisterTransient(key, (_, ref _) => new Resource(), cleanup);
                    break;
                case LifeTime.Scoped:
                    registrar.RegisterScoped(key, (_, ref _) => new Resource(), cleanup);
                    break;
                case LifeTime.Singleton:
                    registrar.RegisterSingleton(key, (_, ref _) => new Resource(), cleanup);
                    break;
            }
        });
        var resource = container.Resolve(key);

        container.Dispose();

        Assert.That(cleanupCount, Is.EqualTo(customCleanup ? 1 : 0));
        Assert.That(resource.DisposeCount, Is.EqualTo(customCleanup ? 0 : 1));
    }

    [Test]
    public void TransientCustomCleanup_ShouldReplaceDispose_AndReceiveEachInstanceOnce()
    {
        var cleanedInstances = new List<Resource>();
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(cleanup: cleanedInstances.Add);
        });
        var first = container.Resolve<Resource>();
        var second = container.Resolve<Resource>();

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(cleanedInstances, Is.Empty);

        container.Dispose();
        container.Dispose();

        Assert.That(cleanedInstances, Is.EquivalentTo(new[] { first, second }));
        Assert.That(first.DisposeCount, Is.Zero);
        Assert.That(second.DisposeCount, Is.Zero);
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
            (registrar, cleanup) => registrar.Register(key, () => new Service(), LifeTime.Scoped, cleanup));
        yield return Registration("GeneralContainerFactoryWithKey", key,
            (registrar, cleanup) => registrar.Register(key, (_, ref _) => new Service(), LifeTime.Scoped, cleanup));
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
            (registrar, cleanup) => registrar.Register<IService, Service>(LifeTime.Scoped, cleanup));
        yield return Registration("GeneralImplementationWithKey", key,
            (registrar, cleanup) => registrar.Register<IService, Service>(key, LifeTime.Singleton, cleanup));
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
