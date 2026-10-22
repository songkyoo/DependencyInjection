// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerTransientTests
{
    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class Dependency(Resource resource)
    {
        public Resource Resource { get; } = resource;

        public int CleanupCount { get; set; }
    }

    private sealed class Service(Dependency dependency)
    {
        public Dependency Dependency { get; } = dependency;

        public int CleanupCount { get; set; }
    }

    [Test]
    public void InheritedTransient_ShouldBeDisposedByRequestingContainer_OnlyOnce()
    {
        var builder = new DependencyContainerBuilder();

        builder.RegisterTransient<Resource>();

        using var root = builder.Build();
        using var child = root.CreateScope();
        using var grandchild = child.CreateScope();
        var rootResource = root.Resolve<Resource>();
        var childResource = child.Resolve<Resource>();
        var first = grandchild.Resolve<Resource>();
        var second = grandchild.Resolve<Resource>();

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(first.DisposeCount, Is.Zero);
        Assert.That(second.DisposeCount, Is.Zero);

        grandchild.Dispose();
        grandchild.Dispose();

        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
        Assert.That(childResource.DisposeCount, Is.Zero);
        Assert.That(rootResource.DisposeCount, Is.Zero);

        child.Dispose();

        Assert.That(childResource.DisposeCount, Is.EqualTo(1));
        Assert.That(rootResource.DisposeCount, Is.Zero);

        root.Dispose();

        Assert.That(rootResource.DisposeCount, Is.EqualTo(1));
        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void NestedTransientDependencies_ShouldFollowServiceOwnership(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var builder = new DependencyContainerBuilder();

        builder.RegisterTransient<Resource>();
        builder.RegisterTransient(
            factory: (resolver, ref _) => new Dependency(resolver.Resolve<Resource>()),
            cleanup: dependency => dependency.CleanupCount++
        );
        builder.Register(
            factory: (resolver, ref _) => new Service(resolver.Resolve<Dependency>()), lifeTime,
            cleanup: service => service.CleanupCount++
        );

        using var root = builder.Build();
        using var child = root.CreateScope();
        using var sibling = root.CreateScope();
        var service = child.Resolve<Service>();
        var siblingService = sibling.Resolve<Service>();
        var dependency = service.Dependency;
        var resource = dependency.Resource;

        Assert.That(siblingService, lifeTime == LifeTime.Singleton ? Is.SameAs(service) : Is.Not.SameAs(service));
        Assert.That(dependency.CleanupCount, Is.Zero);
        Assert.That(resource.DisposeCount, Is.Zero);

        sibling.Dispose();

        Assert.That(service.CleanupCount, Is.Zero);
        Assert.That(dependency.CleanupCount, Is.Zero);
        Assert.That(resource.DisposeCount, Is.Zero);

        child.Dispose();

        var cleanedByChild = lifeTime == LifeTime.Singleton ? 0 : 1;

        Assert.That(service.CleanupCount, Is.EqualTo(cleanedByChild));
        Assert.That(dependency.CleanupCount, Is.EqualTo(cleanedByChild));
        Assert.That(resource.DisposeCount, Is.EqualTo(cleanedByChild));

        root.Dispose();

        Assert.That(service.CleanupCount, Is.EqualTo(1));
        Assert.That(dependency.CleanupCount, Is.EqualTo(1));
        Assert.That(resource.DisposeCount, Is.EqualTo(1));
        Assert.That(siblingService.CleanupCount, Is.EqualTo(1));
        Assert.That(siblingService.Dependency.CleanupCount, Is.EqualTo(1));
        Assert.That(siblingService.Dependency.Resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void AllLifetimesAndCollectedDisposable_ShouldBeDisposedByContainer()
    {
        var collected = new Resource();
        var builder = new DependencyContainerBuilder();

        builder.RegisterTransient(tag: "transient",
            factory: (_, ref collector) =>
            {
                collector.Add(collected);
                return new Resource();
            });
        builder.RegisterScoped<Resource>(tag: "scoped");
        builder.RegisterSingleton<Resource>(tag: "singleton");

        using var container = builder.Build();
        var transient = container.Resolve<Resource>("transient");
        var scoped = container.Resolve<Resource>("scoped");
        var singleton = container.Resolve<Resource>("singleton");
        container.Dispose();
        container.Dispose();

        Assert.That(transient.DisposeCount, Is.EqualTo(1));
        Assert.That(scoped.DisposeCount, Is.EqualTo(1));
        Assert.That(singleton.DisposeCount, Is.EqualTo(1));
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void InvalidTransientFactoryResult_ShouldNotRunCleanup([Values] bool returnsWrongType)
    {
        var resource = new Resource();
        var cleanupCount = 0;
        var builder = new DependencyContainerBuilder();
        IDependencyRegistrar registrar = builder;

        registrar.RegisterTransient(
            type: typeof(Service),
            tag: null,
            factory: (_, ref _) => returnsWrongType ? resource : null!,
            cleanup: _ => cleanupCount++
        );

        using var container = builder.Build();

        Assert.That(container.Resolve<Service>, Throws.InvalidOperationException);

        container.Dispose();

        Assert.That(cleanupCount, Is.Zero);
        Assert.That(resource.DisposeCount, Is.Zero);
    }
}
