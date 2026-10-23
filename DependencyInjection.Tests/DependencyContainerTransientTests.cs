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
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>();
        });
        using var child = root.CreateScope(_ => { });
        using var grandchild = child.CreateScope(_ => { });
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
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>();
            registrar.RegisterTransient(
                factory: (resolver, ref _) => new Dependency(resolver.Resolve<Resource>()),
                cleanup: dependency => dependency.CleanupCount++
            );
            registrar.Register(
                factory: (resolver, ref _) => new Service(resolver.Resolve<Dependency>()), lifetime,
                cleanup: service => service.CleanupCount++
            );
        });
        using var child = root.CreateScope(_ => { });
        using var sibling = root.CreateScope(_ => { });
        var service = child.Resolve<Service>();
        var siblingService = sibling.Resolve<Service>();
        var dependency = service.Dependency;
        var resource = dependency.Resource;

        Assert.That(siblingService, lifetime == Lifetime.Singleton ? Is.SameAs(service) : Is.Not.SameAs(service));
        Assert.That(dependency.CleanupCount, Is.Zero);
        Assert.That(resource.DisposeCount, Is.Zero);

        sibling.Dispose();

        Assert.That(service.CleanupCount, Is.Zero);
        Assert.That(dependency.CleanupCount, Is.Zero);
        Assert.That(resource.DisposeCount, Is.Zero);

        child.Dispose();

        var cleanedByChild = lifetime == Lifetime.Singleton ? 0 : 1;

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
}
