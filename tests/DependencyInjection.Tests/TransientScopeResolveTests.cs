// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class TransientScopeResolveTests
{
    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed record Service(Resource Dependency, Resource Shared);

    private sealed record FallbackService(Resource Resource);

    [Test]
    public void SharedServices_ShouldReuseBaseContainerCaches([Values] bool useChild)
    {
        var scopedTag = "scoped";
        var singletonTag = "singleton";
        var creations = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(CreateResource, Lifetime.Scoped, tag: scopedTag);
            registrar.Register(CreateResource, Lifetime.Singleton, tag: singletonTag);
        });
        using var child = root.CreateScope();
        var container = useChild ? child : root;
        var expectedScoped = container.Resolve<Resource>(scopedTag);
        var expectedSingleton = root.Resolve<Resource>(singletonTag);
        var scope = container.CreateTransientScope();

        Assert.That(creations, Is.EqualTo(3));
        Assert.That(scope.Contains<Resource>(scopedTag), Is.True);
        Assert.That(scope.Contains<Resource>(singletonTag), Is.True);
        Assert.That(scope.Contains<Service>(), Is.False);
        Assert.That(scope.Resolve<Resource>(scopedTag), Is.SameAs(expectedScoped));
        Assert.That(scope.Resolve<Resource>(singletonTag), Is.SameAs(expectedSingleton));

        scope.Dispose();

        Assert.That(creations, Is.EqualTo(3));
        Assert.That(expectedScoped.DisposeCount, Is.Zero);
        Assert.That(expectedSingleton.DisposeCount, Is.Zero);

        #region Local Functions
        Resource CreateResource()
        {
            creations++;

            return new Resource();
        }
        #endregion
    }

    [Test]
    public void SharedServiceTransientDependencies_ShouldRemainOwnedByContainer(
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime,
        [Values] bool useChild
    )
    {
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(factory: () => new Resource());
            registrar.Register<Service>(factory: (resolver, ref _) =>
                new Service(resolver.Resolve<Resource>(), resolver.Resolve<Resource>()), lifetime);
        });
        using var child = root.CreateScope();
        var container = useChild ? child : root;
        var scope = container.CreateTransientScope();
        var service = scope.Resolve<Service>();

        Assert.That(service, Is.SameAs(container.Resolve<Service>()));

        scope.Dispose();

        Assert.That(service.Dependency.DisposeCount, Is.Zero);
        Assert.That(service.Shared.DisposeCount, Is.Zero);

        child.Dispose();

        var cleanedByChild = useChild && lifetime == Lifetime.Scoped ? 1 : 0;

        Assert.That(service.Dependency.DisposeCount, Is.EqualTo(cleanedByChild));
        Assert.That(service.Shared.DisposeCount, Is.EqualTo(cleanedByChild));

        root.Dispose();

        Assert.That(service.Dependency.DisposeCount, Is.EqualTo(1));
        Assert.That(service.Shared.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void NestedTransientsAndCollectors_ShouldBelongOnlyToRequestingScope([Values] bool useChild)
    {
        var sharedTag = "shared";
        object? dependencyTag = null;
        object? serviceTag = null;
        var resolvers = new List<IDependencyResolver>();
        var cleanups = new List<string>();
        var dependencyId = 0;
        var serviceId = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>(factory: () => new Resource(), tag: sharedTag);
            registrar.RegisterTransient<Resource>(
                factory: (resolver, ref collector) =>
                {
                    resolvers.Add(resolver);

                    var id = ++dependencyId;

                    collector.Add(new object(), _ => cleanups.Add($"dependency-collected-{id}"));

                    return new Resource();
                },
                cleanup: resource => resource.Dispose()
            );
            registrar.RegisterTransient<Service>(
                factory: (resolver, ref collector) =>
                {
                    resolvers.Add(resolver);

                    var id = ++serviceId;
                    var dependency = resolver.Resolve<Resource>(dependencyTag);
                    var shared = resolver.Resolve<Resource>(sharedTag);

                    collector.Add(new object(), _ => cleanups.Add($"service-collected-{id}"));

                    return new Service(dependency, shared);
                },
                cleanup: service =>
                {
                    Assert.That(service.Dependency.DisposeCount, Is.Zero);
                    Assert.That(service.Shared.DisposeCount, Is.Zero);
                    cleanups.Add("service");
                }
            );
        });
        using var child = root.CreateScope();
        var container = useChild ? child : root;
        var firstScope = container.CreateTransientScope();
        var secondScope = container.CreateTransientScope();
        var first = firstScope.Resolve<Service>(serviceTag);
        var second = secondScope.Resolve<Service>(serviceTag);
        var direct = container.Resolve<Service>(serviceTag);

        Assert.That(first.Dependency, Is.Not.SameAs(second.Dependency));
        Assert.That(first.Shared, Is.SameAs(container.Resolve<Resource>(sharedTag)));
        Assert.That(second.Shared, Is.SameAs(first.Shared));
        Assert.That(resolvers[0], Is.SameAs(firstScope));
        Assert.That(resolvers[2], Is.SameAs(secondScope));
        Assert.That(resolvers[0], Is.SameAs(resolvers[1]));
        Assert.That(resolvers[2], Is.SameAs(resolvers[3]));
        Assert.That(resolvers[0], Is.Not.SameAs(resolvers[2]));
        Assert.That(resolvers[0], Is.Not.SameAs(container));
        Assert.That(resolvers[4], Is.SameAs(container));

        firstScope.Dispose();

        Assert.That(cleanups, Is.EqualTo(new[]
        {
            "service-collected-1", "service", "dependency-collected-1"
        }));
        Assert.That(first.Dependency.DisposeCount, Is.EqualTo(1));
        Assert.That(second.Dependency.DisposeCount, Is.Zero);
        Assert.That(direct.Dependency.DisposeCount, Is.Zero);

        secondScope.Dispose();

        Assert.That(second.Dependency.DisposeCount, Is.EqualTo(1));
        Assert.That(direct.Dependency.DisposeCount, Is.Zero);
        Assert.That(first.Shared.DisposeCount, Is.Zero);
    }

    [Test]
    public void FallbackResolver_ShouldBeSameScope_AndOwnNestedTransient([Values] bool useNamedTag)
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(factory: () => new Resource());
        });
        var scope = container.CreateTransientScope();
        IDependencyResolver? capturedResolver = null;
        Func<IDependencyResolver, FallbackService> factory = resolver =>
        {
            capturedResolver = resolver;

            return new FallbackService(resolver.Resolve<Resource>());
        };

        var service = useNamedTag
            ? scope.ResolveOrElse(factory, tag: "missing")
            : scope.ResolveOrElse(factory);

        Assert.That(capturedResolver, Is.SameAs(scope));
        Assert.That(scope.Contains<FallbackService>(), Is.False);
        Assert.That(scope.ResolveOrNull<FallbackService>(), Is.Null);
        Assert.That(service.Resource.DisposeCount, Is.Zero);

        scope.Dispose();

        Assert.That(service.Resource.DisposeCount, Is.EqualTo(1));
        Assert.That(() => capturedResolver!.Resolve<Resource>(), Throws.TypeOf<ObjectDisposedException>());
    }
}
