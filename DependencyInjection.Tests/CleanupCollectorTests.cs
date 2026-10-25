// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToModifiedClosure
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class CleanupCollectorTests
{
    private sealed class Resource(Action? onDispose = null) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            onDispose?.Invoke();
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(17)]
    public void CollectedEntries_ShouldSurviveArrayGrowth_AndRunBeforeService(int count)
    {
        var calls = new List<int>();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<object>(
                factory: (_, ref collector) =>
                {
                    for (var index = 0; index < count; index++)
                    {
                        collector.Add(index, instance => calls.Add((int)instance));
                    }
                    return new object();
                },
                cleanup: _ =>
                {
                    calls.Add(-1);
                }
            );
        });

        container.Resolve<object>();
        container.Resolve<object>();

        Assert.That(calls, Is.Empty);

        container.Dispose();

        Assert.That(calls, Is.EqualTo(Enumerable.Range(0, count).Reverse().Append(-1)));
    }

    [Test]
    public void FailedFactory_ShouldCleanCollectedEntries_PreserveError_AndAllowRetry(
        [Values] bool cleanupFails
    )
    {
        var factoryFailure = new InvalidOperationException("Factory failed.");
        var cleanupFailure = new ArgumentException("Collected cleanup failed.");
        var failCreation = true;
        var calls = new List<string>();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<object>(
                factory: (_, ref collector) =>
                {
                    collector.Add(new Resource(() => calls.Add("first")));
                    collector.Add(new object(), _ =>
                    {
                        calls.Add("second");

                        if (cleanupFails)
                        {
                            throw cleanupFailure;
                        }
                    });

                    if (failCreation)
                    {
                        throw factoryFailure;
                    }

                    return new object();
                },
                Lifetime.Transient,
                cleanup: _ => calls.Add("service")
            );
        });

        if (cleanupFails)
        {
            var exception = Assert.Throws<AggregateException>(() => container.Resolve<object>());

            Assert.That(exception!.InnerExceptions, Is.EqualTo(new Exception[] { factoryFailure, cleanupFailure }));
        }
        else
        {
            var exception = Assert.Throws<InvalidOperationException>(() => container.Resolve<object>());

            Assert.That(exception, Is.SameAs(factoryFailure));
        }

        Assert.That(calls, Is.EqualTo(new[] { "second", "first" }));

        failCreation = false;

        var service = container.Resolve<object>();

        Assert.That(service, Is.Not.Null);

        if (cleanupFails)
        {
            var exception = Assert.Throws<AggregateException>(container.Dispose);

            Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { cleanupFailure }));
        }
        else
        {
            container.Dispose();
        }

        Assert.That(calls, Is.EqualTo(new[] { "second", "first", "second", "first", "service" }));
        Assert.That(container.Dispose, Throws.Nothing);
    }

    [Test]
    public void NullTransientFactoryResult_ShouldCleanCollector_ReportTag_AndAllowRetry([Values] bool useDefaultTag)
    {
        var tag = useDefaultTag ? null : "service";
        var returnNull = true;
        var collected = new Resource();
        var serviceCleanupCount = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<string>(
                factory: (_, ref collector) =>
                {
                    if (returnNull)
                    {
                        collector.Add(collected);
                        return null!;
                    }

                    return "Hello";
                },
                tag,
                cleanup: _ => serviceCleanupCount++
            );
        });
        using var child = root.CreateScope();

        var error = Assert.Throws<InvalidOperationException>(() => child.Resolve<string>(tag));

        Assert.That(error!.Message, Does.Contain("Factory returned null"));
        Assert.That(error.Message, Does.Contain(tag ?? "null"));
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(serviceCleanupCount, Is.Zero);

        returnNull = false;

        Assert.That(child.Resolve<string>(tag), Is.EqualTo("Hello"));

        child.Dispose();
        root.Dispose();

        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(serviceCleanupCount, Is.EqualTo(1));
    }

    [Test]
    public void FailedFactory_ShouldLeaveResolvedDependencyOwnedByContainer()
    {
        var dependency = new Resource();
        var collected = new Resource();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped(factory: () => dependency);
            registrar.RegisterTransient<object>(factory: (resolver, ref collector) =>
            {
                collector.Add(collected);
                resolver.Resolve<Resource>();

                throw new InvalidOperationException("Factory failed.");
            });
        });

        Assert.That(() => container.Resolve<object>(), Throws.InvalidOperationException);
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(dependency.DisposeCount, Is.Zero);
        Assert.That(container.Resolve<Resource>(), Is.SameAs(dependency));

        container.Dispose();

        Assert.That(dependency.DisposeCount, Is.EqualTo(1));
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
    }
}
