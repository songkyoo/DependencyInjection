// ReSharper disable AccessToDisposedClosure
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
    public void MixedCleanupEntries_ShouldRunInReverseRecordingOrder_OnlyOnce()
    {
        var calls = new List<string>();
        var builder = new DependencyContainerBuilder();

        builder.RegisterSingleton(factory: () => new Resource(() => calls.Add("singleton")));
        builder.RegisterScoped<object>(tag: "scoped", cleanup: _ => calls.Add("scoped"));
        builder.RegisterTransient<object>(tag: "transient", cleanup: _ => calls.Add("transient"));

        var container = builder.Build();

        container.AddDisposable(new Resource(() => calls.Add("external-first")));
        container.Resolve<Resource>();
        container.AddDisposable(new Resource(() => calls.Add("external-middle")));
        container.Resolve<object>("scoped");
        container.Resolve<object>("transient");
        container.AddDisposable(new Resource(() => calls.Add("external-last")));
        container.Resolve<object>("scoped");
        container.Resolve<Resource>();

        container.Dispose();
        container.Dispose();

        Assert.That(calls, Is.EqualTo(new[]
        {
            "external-last", "transient", "scoped", "external-middle", "singleton", "external-first"
        }));
    }

    [Test]
    public void ServiceCleanup_ShouldRunBeforeItsTransientDependency(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var calls = new List<string>();
        var builder = new DependencyContainerBuilder();

        builder.RegisterTransient(factory: () => new Resource(() => calls.Add("dependency")));
        builder.Register(
            factory: resolver => new Service(resolver.Resolve<Resource>()), lifeTime,
            cleanup: service =>
            {
                Assert.That(service.Resource.IsDisposed, Is.False);
                calls.Add("service");
            }
        );

        var container = builder.Build();

        container.Resolve<Service>();
        container.Dispose();

        Assert.That(calls, Is.EqualTo(new[] { "service", "dependency" }));
    }

    [Test]
    public void CleanupFailures_ShouldBeAggregated_AfterEveryCallback([Values] bool multipleFailures)
    {
        var calls = new List<string>();
        var firstFailure = new InvalidOperationException("Default Dispose failed.");
        var lastFailure = new AggregateException(new ArgumentException("Custom cleanup failed."));
        var builder = new DependencyContainerBuilder();

        builder.RegisterSingleton<object>(cleanup: _ =>
        {
            calls.Add("custom");

            if (multipleFailures)
            {
                throw lastFailure;
            }
        });

        var container = builder.Build();

        container.AddDisposable(new Resource(() => calls.Add("oldest")));
        container.Resolve<object>();
        container.AddDisposable(new Resource(() => calls.Add("middle")));
        container.AddDisposable(new Resource(() =>
        {
            calls.Add("default");
            throw firstFailure;
        }));
        container.AddDisposable(new Resource(() => calls.Add("newest")));

        var exception = Assert.Throws<AggregateException>(container.Dispose);

        Assert.That(calls, Is.EqualTo(new[] { "newest", "default", "middle", "custom", "oldest" }));

        var expectedFailures = multipleFailures
            ? new Exception[] { firstFailure, lastFailure }
            : new Exception[] { firstFailure };

        Assert.That(exception!.InnerExceptions, Is.EqualTo(expectedFailures));
        Assert.That(container.Dispose, Throws.Nothing);
        Assert.That(calls, Has.Count.EqualTo(5));
        Assert.That(container.Resolve<object>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(container.Contains<object>, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void DisposeDuringCleanup_ShouldNotRepeatCallbacks()
    {
        var container = new DependencyContainerBuilder().Build();
        var calls = new List<string>();

        container.AddDisposable(new Resource(() => calls.Add("oldest")));
        container.AddDisposable(new Resource(() =>
        {
            calls.Add("reentrant");
            container.Dispose();
        }));
        container.Dispose();

        Assert.That(calls, Is.EqualTo(new[] { "reentrant", "oldest" }));
    }

    [Test]
    public void FailedDisposal_ShouldReleaseCachedAndTransientInstances(
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var (container, service, resource) = CreateDisposedContainerWithWeakReferences(lifeTime);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(service.IsAlive, Is.False);
        Assert.That(resource.IsAlive, Is.False);

        GC.KeepAlive(container);

        #region Local Functions
        [MethodImpl(MethodImplOptions.NoInlining)]
        static (DependencyContainer Container, WeakReference Service, WeakReference Resource)
            CreateDisposedContainerWithWeakReferences(LifeTime lifeTime)
        {
            var builder = new DependencyContainerBuilder();

            builder.RegisterTransient(factory: () => new Resource(static () => { }));
            builder.Register(factory: resolver => new Service(resolver.Resolve<Resource>()), lifeTime);

            var container = builder.Build();
            var service = container.Resolve<Service>();
            var serviceReference = new WeakReference(service);
            var resourceReference = new WeakReference(service.Resource);

            container.AddDisposable(new Resource(static () => throw new InvalidOperationException("Cleanup failed.")));

            Assert.Throws<AggregateException>(container.Dispose);

            return (container, serviceReference, resourceReference);
        }
        #endregion
    }
}
