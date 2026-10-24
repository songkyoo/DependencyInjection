using System.Runtime.CompilerServices;
// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToModifiedClosure

namespace Macaron.DependencyInjection.Tests;

public class TransientScopeDisposalTests
{
    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed record Service(Resource Dependency);

    [Test]
    public void AliasedScopeAndCapturedResolver_ShouldRemainInvalidAfterSlotReuse()
    {
        IDependencyResolver? capturedResolver = null;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(factory: (resolver, ref _) =>
            {
                capturedResolver = resolver;

                return new Resource();
            });
        });
        var first = container.CreateTransientScope();
        var alias = first;
        var firstResource = first.Resolve<Resource>(null);
        var oldResolver = capturedResolver!;

        Assert.That(oldResolver, Is.SameAs(first));

        first.Dispose();

        using var second = container.CreateTransientScope();
        var secondResource = second.Resolve<Resource>(null);

        Assert.That(alias.Dispose, Throws.Nothing);
        Assert.That(() => alias.Resolve<Resource>(null), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => alias.Contains<Resource>(null), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => oldResolver.Resolve<Resource>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => oldResolver.Contains<Resource>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(firstResource.DisposeCount, Is.EqualTo(1));
        Assert.That(secondResource.DisposeCount, Is.Zero);
        Assert.That(second.Contains<Resource>(null), Is.True);
    }

    [Test]
    public void GrowingSlotsAndReusingHoles_ShouldKeepScopesIndependent()
    {
        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Resource>());
        var scopes = new List<TransientScope>();
        var resources = new List<Resource>();

        for (var i = 0; i < 12; i++)
        {
            var scope = container.CreateTransientScope();

            scopes.Add(scope);
            resources.Add(scope.Resolve<Resource>(null));
        }

        for (var i = 0; i < 12; i += 2)
        {
            scopes[i].Dispose();
        }

        for (var i = 0; i < 6; i++)
        {
            var scope = container.CreateTransientScope();

            scopes.Add(scope);
            resources.Add(scope.Resolve<Resource>(null));
        }

        for (var i = 0; i < 12; i += 2)
        {
            scopes[i].Dispose();

            Assert.That(() => scopes[i].Contains<Resource>(null), Throws.TypeOf<ObjectDisposedException>());
        }

        Assert.That(resources.Skip(12).All(resource => resource.DisposeCount == 0), Is.True);

        container.Dispose();

        Assert.That(resources.All(resource => resource.DisposeCount == 1), Is.True);

        foreach (var scope in scopes)
        {
            Assert.That(scope.Dispose, Throws.Nothing);
            Assert.That(() => scope.Contains<Resource>(null), Throws.TypeOf<ObjectDisposedException>());
        }
    }

    [Test]
    public void CleanupFailures_ShouldPreserveErrors_AndAttemptEveryEntry([Values] bool multipleFailures)
    {
        var events = new List<string>();
        var collectedFailure = new AggregateException(new ArgumentException("Collected cleanup failed."));
        var serviceFailure = new InvalidOperationException("Service cleanup failed.");
        IDependencyResolver? capturedResolver = null;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<object>(factory: (resolver, ref collector) =>
            {
                capturedResolver = resolver;
                collector.Add(new object(), _ => events.Add("first"));
                collector.Add(new object(), _ =>
                {
                    events.Add("second");
                    throw collectedFailure;
                });

                return new object();
            }, cleanup: _ =>
            {
                events.Add("service");

                if (multipleFailures)
                {
                    throw serviceFailure;
                }
            });
        });
        var scope = container.CreateTransientScope();

        scope.Resolve<object>(null);

        var exception = Assert.Throws<AggregateException>(scope.Dispose);

        Assert.That(events, Is.EqualTo(new[] { "second", "first", "service" }));
        Assert.That(exception!.InnerExceptions, Is.EqualTo(multipleFailures
            ? new Exception[] { collectedFailure, serviceFailure }
            : new Exception[] { collectedFailure }));
        Assert.That(scope.Dispose, Throws.Nothing);
        Assert.That(() => capturedResolver!.Resolve<object>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => scope.Contains<object>(null), Throws.TypeOf<ObjectDisposedException>());

        using var replacement = container.CreateTransientScope();

        Assert.That(replacement.Contains<object>(null), Is.True);
    }

    [Test]
    public void ContainerDisposal_ShouldCleanEveryScopeBeforeOwnEntries(
        [Values] bool scopeCleanupFails,
        [Values] bool containerCleanupFails
    )
    {
        var events = new List<string>();
        var shared = new Resource();
        var scopeFailure = new ArgumentException("Scope cleanup failed.");
        var containerFailure = new InvalidOperationException("Container cleanup failed.");
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(shared, cleanup: resource =>
            {
                events.Add("shared");
                resource.Dispose();

                if (containerCleanupFails)
                {
                    throw containerFailure;
                }
            });
            registrar.RegisterTransient<Service>(factory: (resolver, ref _) => new Service(resolver.Resolve<Resource>()),
                cleanup: service =>
                {
                    Assert.That(service.Dependency.DisposeCount, Is.Zero);
                    events.Add("scope");

                    if (scopeCleanupFails)
                    {
                        throw scopeFailure;
                    }
                });
        });
        var first = container.CreateTransientScope();
        var second = container.CreateTransientScope();

        first.Resolve<Service>(null);
        second.Resolve<Service>(null);

        if (scopeCleanupFails || containerCleanupFails)
        {
            var exception = Assert.Throws<AggregateException>(container.Dispose);
            var expectedCount = (scopeCleanupFails ? 2 : 0) + (containerCleanupFails ? 1 : 0);

            Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(expectedCount));

            if (scopeCleanupFails)
            {
                foreach (var scopeException in exception.InnerExceptions.Take(2).Cast<AggregateException>())
                {
                    Assert.That(scopeException.InnerExceptions, Is.EqualTo(new[] { scopeFailure }));
                }
            }

            if (containerCleanupFails)
            {
                Assert.That(exception.InnerExceptions[^1], Is.SameAs(containerFailure));
            }
        }
        else
        {
            Assert.That(container.Dispose, Throws.Nothing);
        }

        Assert.That(events, Is.EqualTo(new[] { "scope", "scope", "shared" }));
        Assert.That(shared.DisposeCount, Is.EqualTo(1));
        Assert.That(first.Dispose, Throws.Nothing);
        Assert.That(second.Dispose, Throws.Nothing);
        Assert.That(container.Dispose, Throws.Nothing);
        Assert.That(() => first.Resolve<Service>(null), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => second.Contains<Resource>(null), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(container.CreateTransientScope, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void SameReferenceReturnedToDifferentScopes_ShouldKeepSeparateCleanupEntries()
    {
        var resource = new Resource();
        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient(() => resource));
        var first = container.CreateTransientScope();
        var second = container.CreateTransientScope();

        first.Resolve<Resource>(null);
        first.Resolve<Resource>(null);
        second.Resolve<Resource>(null);

        first.Dispose();
        Assert.That(resource.DisposeCount, Is.EqualTo(2));

        second.Dispose();
        container.Dispose();
        Assert.That(resource.DisposeCount, Is.EqualTo(3));
    }

    [Test]
    public void FailedFactory_ShouldCleanOnlyItsCollector_AndAllowRetry(
        [Values] bool nullResult,
        [Values] bool cleanupFails
    )
    {
        var fail = true;
        var dependencies = new List<Resource>();
        var events = new List<string>();
        var factoryFailure = new ArgumentException("Factory failed.");
        var cleanupFailure = new InvalidOperationException("Collector cleanup failed.");
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(
                factory: () =>
                {
                    var resource = new Resource();

                    dependencies.Add(resource);

                    return resource;
                }
            );
            registrar.RegisterTransient<Service>(
                factory: (resolver, ref collector) =>
                {
                    var dependency = resolver.Resolve<Resource>();
                    var failedAttempt = fail;

                    collector.Add(new object(), _ =>
                    {
                        events.Add(failedAttempt ? "failed-collected" : "successful-collected");

                        if (failedAttempt && cleanupFails)
                        {
                            throw cleanupFailure;
                        }
                    });

                    if (fail)
                    {
                        return nullResult ? null! : throw factoryFailure;
                    }

                    return new Service(dependency);
                },
                cleanup: _ => events.Add("service")
            );
        });
        var scope = container.CreateTransientScope();
        var exception = Assert.Catch(() => scope.Resolve<Service>(null));

        if (cleanupFails)
        {
            Assert.That(exception, Is.TypeOf<AggregateException>());

            var errors = ((AggregateException)exception!).InnerExceptions;

            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(errors[1], Is.SameAs(cleanupFailure));

            exception = errors[0];
        }

        Assert.That(exception, nullResult ? Is.TypeOf<InvalidOperationException>() : Is.SameAs(factoryFailure));
        Assert.That(events, Is.EqualTo(new[] { "failed-collected" }));
        Assert.That(dependencies[0].DisposeCount, Is.Zero);

        fail = false;

        scope.Resolve<Service>(null);
        scope.Dispose();

        Assert.That(dependencies.All(resource => resource.DisposeCount == 1), Is.True);
        Assert.That(events, Is.EqualTo(new[] { "failed-collected", "successful-collected", "service" }));
    }

    [Test]
    public void DisposedScope_ShouldReleaseContainerServicesAndCollectedEntries(
        [Values] bool disposeContainerFirst,
        [Values] bool cleanupFails
    )
    {
        var (scope, resolver, references) = CreateDisposedScope(disposeContainerFirst, cleanupFails);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        foreach (var reference in references)
        {
            Assert.That(reference.IsAlive, Is.False);
        }

        GC.KeepAlive(scope);
        GC.KeepAlive(resolver);

        #region Local Functions
        [MethodImpl(MethodImplOptions.NoInlining)]
        static (TransientScope, IDependencyResolver, WeakReference[]) CreateDisposedScope(
            bool disposeContainerFirst,
            bool cleanupFails
        )
        {
            IDependencyResolver? resolver = null;
            Resource? collected = null;
            var container = DependencyContainerFactory.Create(registrar =>
            {
                registrar.RegisterTransient<Resource>(factory: (currentResolver, ref collector) =>
                {
                    resolver = currentResolver;
                    collected = new Resource();
                    collector.Add(collected);
                    collector.Add(new object(), _ =>
                    {
                        if (cleanupFails)
                        {
                            throw new InvalidOperationException("Cleanup failed.");
                        }
                    });

                    return new Resource();
                });
            });
            var scope = container.CreateTransientScope();
            var resource = scope.Resolve<Resource>(null);
            var references = new[] { new WeakReference(container), new WeakReference(resource), new WeakReference(collected!) };

            if (cleanupFails)
            {
                Assert.Throws<AggregateException>(disposeContainerFirst ? container.Dispose : scope.Dispose);
            }
            else if (disposeContainerFirst)
            {
                container.Dispose();
            }
            else
            {
                scope.Dispose();
            }

            return (scope, resolver!, references);
        }
        #endregion
    }
}
