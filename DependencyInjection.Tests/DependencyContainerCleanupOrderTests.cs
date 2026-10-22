using System.Runtime.CompilerServices;

namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerCleanupOrderTests
{
    private sealed class Resource(Action onDispose) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            onDispose();
        }
    }

    private sealed class Service(Resource resource)
    {
        public Resource Resource { get; } = resource;
    }

    [Test]
    public void CollectedCleanup_ShouldRunBeforeService_AndAfterDependentService(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var calls = new List<string>();
        var builder = new DependencyContainerBuilder();

        builder.Register<Resource>(
            factory: (_, ref collector) =>
            {
                var resource = new Resource(() => calls.Add("dependency"));

                collector.Add(new object(), _ =>
                {
                    Assert.That(resource.IsDisposed, Is.False);
                    calls.Add("dependency-subscription");
                });

                return resource;
            },
            lifeTime
        );
        builder.Register<Service>(
            factory: (resolver, ref collector) =>
            {
                collector.Add(new Resource(() => calls.Add("first")));

                var service = new Service(resolver.Resolve<Resource>());

                collector.Add(new object(), _ => calls.Add("second"));

                return service;
            },
            lifeTime,
            cleanup: service =>
            {
                Assert.That(service.Resource.IsDisposed, Is.False);
                calls.Add("service");
            }
        );

        var container = builder.Build();

        container.Resolve<Service>();
        container.Dispose();
        container.Dispose();

        Assert.That(calls, Is.EqualTo(new[]
        {
            "second", "first", "service", "dependency-subscription", "dependency"
        }));
    }

    [Test]
    public void CleanupFailures_ShouldBeAggregated_AfterEveryCallback([Values] bool multipleFailures)
    {
        var calls = new List<string>();
        var firstFailure = new InvalidOperationException("Default Dispose failed.");
        var lastFailure = new AggregateException(new ArgumentException("Custom cleanup failed."));
        var builder = new DependencyContainerBuilder();

        builder.RegisterSingleton<object>(
            factory: (_, ref collector) =>
            {
                collector.Add(new Resource(() => calls.Add("middle")));
                collector.Add(new Resource(() =>
                {
                    calls.Add("default");
                    throw firstFailure;
                }));
                collector.Add(new Resource(() => calls.Add("newest")));

                return new object();
            },
            cleanup: _ =>
            {
                calls.Add("service");

                if (multipleFailures)
                {
                    throw lastFailure;
                }
            }
        );

        var container = builder.Build();

        container.Resolve<object>();

        var exception = Assert.Throws<AggregateException>(container.Dispose);

        Assert.That(calls, Is.EqualTo(new[] { "newest", "default", "middle", "service" }));

        var expectedFailures = multipleFailures
            ? new Exception[] { firstFailure, lastFailure }
            : new Exception[] { firstFailure };

        Assert.That(exception!.InnerExceptions, Is.EqualTo(expectedFailures));
        Assert.That(container.Dispose, Throws.Nothing);
        Assert.That(calls, Has.Count.EqualTo(4));
        Assert.That(container.Resolve<object>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(container.Contains<object>, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void DisposeDuringCleanup_ShouldNotRepeatCallbacks()
    {
        var calls = new List<string>();
        var builder = new DependencyContainerBuilder();

        builder.RegisterSingleton<object>(
            factory: (container, ref collector) =>
            {
                collector.Add(new Resource(() =>
                {
                    calls.Add("reentrant");
                    container.Dispose();
                }));

                return new object();
            },
            cleanup: _ => calls.Add("service")
        );

        var container = builder.Build();

        container.Resolve<object>();
        container.Dispose();

        Assert.That(calls, Is.EqualTo(new[] { "reentrant", "service" }));
    }

    [Test]
    public void FailedDisposal_ShouldReleaseServiceAndCollectedInstances(
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var (container, service, resource) = CreateDisposedContainer(lifeTime);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(service.IsAlive, Is.False);
        Assert.That(resource.IsAlive, Is.False);

        GC.KeepAlive(container);

        #region Local Functions
        [MethodImpl(MethodImplOptions.NoInlining)]
        static (DependencyContainer, WeakReference, WeakReference) CreateDisposedContainer(LifeTime lifeTime)
        {
            var builder = new DependencyContainerBuilder();

            builder.Register<Service>(
                factory: (_, ref collector) =>
                {
                    var resource = new Resource(static () => { });

                    collector.Add(resource);
                    collector.Add(new Resource(static () => throw new InvalidOperationException("Cleanup failed.")));

                    return new Service(resource);
                },
                lifeTime
            );

            var container = builder.Build();
            var service = container.Resolve<Service>();
            var serviceReference = new WeakReference(service);
            var resourceReference = new WeakReference(service.Resource);

            Assert.Throws<AggregateException>(container.Dispose);

            return (container, serviceReference, resourceReference);
        }
        #endregion
    }
}
