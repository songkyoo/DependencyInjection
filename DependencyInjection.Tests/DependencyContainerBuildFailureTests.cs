using System.Runtime.CompilerServices;

namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerBuildFailureTests
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

    private sealed record Service(Resource Resource);

    private sealed class CycleA;

    private sealed class CycleB;

    [Test]
    public void InvalidSharedFactoryResult_ShouldFailBuild_CleanCollector_AndRejectServiceOwnership(
        [Values] bool createChild,
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values(null, "service-tag")] string? tag,
        [Values] bool wrongType
    )
    {
        using var parent = DependencyContainerFactory.Create();
        var collected = new Resource();
        var invalidResult = new Resource();
        var serviceCleanups = 0;
        IDependencyContainer? failedContainer = null;

        var exception = Assert.Throws<AggregateException>(() =>
        {
            if (createChild)
            {
                parent.CreateScope(Configure);
            }
            else
            {
                DependencyContainerFactory.Create(Configure);
            }
        });

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(1));
        var validationError = exception.InnerExceptions[0];

        Assert.That(validationError, Is.TypeOf<InvalidOperationException>());
        Assert.That(validationError.Message, Does.Contain(typeof(Service).ToString()));

        if (!wrongType)
        {
            Assert.That(validationError.Message, Does.Contain(Key.Of<Service>(tag).ToString()));
        }

        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(invalidResult.DisposeCount, Is.Zero);
        Assert.That(serviceCleanups, Is.Zero);
        Assert.That(failedContainer!.Contains<Service>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer!.Dispose, Throws.Nothing);
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(parent.Contains<Service>(), Is.False);

        void Configure(IDependencyRegistrar registrar)
        {
            DependencyFactory<object> factory = (container, ref collector) =>
            {
                failedContainer = container;
                collector.Add(collected);

                return wrongType ? invalidResult : null!;
            };

            if (lifeTime == LifeTime.Scoped)
            {
                registrar.RegisterScoped(typeof(Service), tag, factory, _ => serviceCleanups++);
            }
            else
            {
                registrar.RegisterSingleton(typeof(Service), tag, factory, _ => serviceCleanups++);
            }
        }
    }

    [Test]
    public void CycleThroughTransientDependency_ShouldFailBuild_WithResolutionPath(
        [Values] bool createChild,
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        using var parent = DependencyContainerFactory.Create();
        IDependencyContainer? failedContainer = null;
        var firstKey = Key.Of<CycleA>("cycle");
        var secondKey = Key.Of<CycleB>("cycle");

        var exception = Assert.Throws<AggregateException>(() =>
        {
            if (createChild)
            {
                parent.CreateScope(Configure);
            }
            else
            {
                DependencyContainerFactory.Create(Configure);
            }
        });

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(1));
        var cycleError = exception.InnerExceptions[0];

        Assert.That(cycleError, Is.TypeOf<InvalidOperationException>());
        Assert.That(cycleError.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(cycleError.Message, Does.Contain(firstKey.ToString()));
        Assert.That(cycleError.Message, Does.Contain(secondKey.ToString()));
        Assert.That(failedContainer!.Contains<CycleA>, Throws.TypeOf<ObjectDisposedException>());

        void Configure(IDependencyRegistrar registrar)
        {
            registrar.Register(firstKey, factory: (container, ref _) =>
            {
                failedContainer = container;
                container.Resolve(secondKey);

                return new CycleA();
            }, lifeTime);
            registrar.RegisterTransient(secondKey, factory: (container, ref _) =>
            {
                container.Resolve(firstKey);

                return new CycleB();
            });
        }
    }

    [Test]
    public void FailedBuild_ShouldAttemptEveryCleanup_AndPreserveErrorTreeAndOtherScopes(
        [Values] bool createChild,
        [Values] bool collectorCleanupFails,
        [Values] bool rollbackFails
    )
    {
        var scopedInstances = new Dictionary<IDependencyContainer, Resource>();
        using var parent = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>("parent-scoped", factory: (container, ref _) =>
            {
                var resource = new Resource();

                scopedInstances.Add(container, resource);

                return resource;
            });
            registrar.RegisterSingleton("parent-singleton", factory: () => new Resource());
        });
        using var sibling = parent.CreateScope();
        var parentResource = scopedInstances[parent];
        var siblingResource = scopedInstances[sibling];
        var sharedResource = parent.Resolve<Resource>("parent-singleton");
        var creationFailure = new AggregateException(new ArgumentException("Creation failed."));
        var collectorFailure = new AggregateException(new ArgumentException("Collected cleanup failed."));
        var rollbackFailure = new AggregateException(new ArgumentException("Rollback collection failed."));
        var resourceFailure = new ArgumentException("Resource cleanup failed.");
        var cleanups = new List<string>();
        IDependencyContainer? failedContainer = null;
        IDependencyContainer? result = null;

        var exception = Assert.Throws<AggregateException>(() =>
        {
            result = createChild
                ? parent.CreateScope(Configure)
                : DependencyContainerFactory.Create(Configure);
        });

        Assert.That(result, Is.Null);
        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(rollbackFails ? 2 : 1));

        if (collectorCleanupFails)
        {
            var factoryError = exception.InnerExceptions[0] as AggregateException;

            Assert.That(factoryError, Is.Not.Null);
            Assert.That(factoryError!.InnerExceptions, Is.EqualTo(new Exception[] { creationFailure, collectorFailure }));
        }
        else
        {
            Assert.That(exception.InnerExceptions[0], Is.SameAs(creationFailure));
        }

        if (rollbackFails)
        {
            var disposalError = exception.InnerExceptions[1] as AggregateException;

            Assert.That(disposalError, Is.Not.Null);
            Assert.That(disposalError!.InnerExceptions, Is.EqualTo(new Exception[] { rollbackFailure, resourceFailure }));
        }

        Assert.That(cleanups, Is.EqualTo(new[]
        {
            "failed-second", "failed-first", "owned-second", "owned-first", "owned-service"
        }));
        Assert.That(failedContainer!.Contains<Service>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer!.Resolve<Service>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer!.CreateScope, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer!.Dispose, Throws.Nothing);
        Assert.That(cleanups, Has.Count.EqualTo(5));
        Assert.That(parent.Resolve<Resource>("parent-scoped"), Is.SameAs(parentResource));
        Assert.That(sibling.Resolve<Resource>("parent-scoped"), Is.SameAs(siblingResource));
        Assert.That(parentResource.DisposeCount, Is.Zero);
        Assert.That(siblingResource.DisposeCount, Is.Zero);
        Assert.That(sharedResource.DisposeCount, Is.Zero);

        if (createChild)
        {
            Assert.That(scopedInstances[failedContainer!].DisposeCount, Is.EqualTo(1));
        }

        using var retry = parent.CreateScope();

        Assert.That(retry.Resolve<Resource>("parent-singleton"), Is.SameAs(sharedResource));

        #region Local Functions
        void Configure(IDependencyRegistrar registrar)
        {
            registrar.RegisterScoped<Resource>("owned", factory: (_, ref collector) =>
            {
                collector.Add(new object(), _ => cleanups.Add("owned-first"));
                collector.Add(new object(), _ =>
                {
                    cleanups.Add("owned-second");

                    if (rollbackFails)
                    {
                        throw rollbackFailure;
                    }
                });

                return new Resource(() =>
                {
                    cleanups.Add("owned-service");

                    if (rollbackFails)
                    {
                        throw resourceFailure;
                    }
                });
            });
            registrar.RegisterSingleton<Service>(factory: (container, ref collector) =>
            {
                failedContainer = container;
                container.Resolve<Resource>("owned");

                if (createChild)
                {
                    container.Resolve<Resource>("parent-scoped");
                    container.Resolve<Resource>("parent-singleton");
                }

                collector.Add(new object(), _ => cleanups.Add("failed-first"));
                collector.Add(new object(), _ =>
                {
                    cleanups.Add("failed-second");

                    if (collectorCleanupFails)
                    {
                        throw collectorFailure;
                    }
                });

                throw creationFailure;
            });
        }
        #endregion
    }

    [Test]
    public void Rollback_ShouldKeepCleanupEntriesForSameReference_WithoutRecordingParentCacheReturns(
        [Values] bool createChild,
        [Values(LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime
    )
    {
        var protectedResource = new Resource();
        using var parent = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton("parent", protectedResource));
        var sharedResource = new Resource();
        var creationOrder = new List<string>();
        var cleanupOrder = new List<string>();
        var creationFailure = new ArgumentException("Creation failed.");

        var exception = Assert.Throws<AggregateException>(() =>
        {
            if (createChild)
            {
                parent.CreateScope(Configure);
            }
            else
            {
                DependencyContainerFactory.Create(Configure);
            }
        });

        Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { creationFailure }));
        Assert.That(creationOrder, Has.Count.EqualTo(2));
        Assert.That(cleanupOrder, Is.EqualTo(new[] { "failed-collected" }.Concat(creationOrder.AsEnumerable().Reverse())));
        Assert.That(sharedResource.DisposeCount, Is.EqualTo(1));
        Assert.That(protectedResource.DisposeCount, Is.Zero);
        Assert.That(parent.Resolve<Resource>("parent"), Is.SameAs(protectedResource));

        void Configure(IDependencyRegistrar registrar)
        {
            registrar.Register(
                tag: "first",
                factory: () =>
                {
                    creationOrder.Add("first");

                    return sharedResource;
                },
                lifeTime,
                cleanup: resource =>
                {
                    cleanupOrder.Add("first");
                    resource.Dispose();
                }
            );
            registrar.Register<object>(
                tag: "second",
                factory: () =>
                {
                    creationOrder.Add("second");

                    return sharedResource;
                },
                lifeTime,
                cleanup: _ => cleanupOrder.Add("second")
            );
            registrar.RegisterSingleton<Service>(
                factory: (container, ref collector) =>
                {
                    container.Resolve<Resource>("first");
                    container.Resolve<object>("second");

                    if (createChild)
                    {
                        container.Resolve<Resource>("parent");
                        container.Resolve<Resource>("parent");
                    }

                    collector.Add(sharedResource, _ => cleanupOrder.Add("failed-collected"));

                    throw creationFailure;
                }
            );
        }
    }

    [Test]
    public void FailedBuild_ShouldReleaseCachedCollectedAndRegistrationReferences(
        [Values] bool createChild,
        [Values] bool rollbackFails
    )
    {
        var (failedContainer, references) = CreateFailedContainer(createChild, rollbackFails);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        foreach (var (name, reference) in references)
        {
            Assert.That(reference.IsAlive, Is.False, name);
        }

        Assert.That(failedContainer.Contains<Resource>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer.Dispose, Throws.Nothing);

        GC.KeepAlive(failedContainer);


        #region Local Functions
        [MethodImpl(MethodImplOptions.NoInlining)]
        static (IDependencyContainer, Dictionary<string, WeakReference>) CreateFailedContainer(
            bool createChild,
            bool rollbackFails
        )
        {
            var references = new Dictionary<string, WeakReference>();
            var registrationState = new object();
            IDependencyContainer? failedContainer = null;
            var parent = createChild ? DependencyContainerFactory.Create() : null;

            references.Add("registration capture", new WeakReference(registrationState));

            if (parent is not null)
            {
                references.Add("parent", new WeakReference(parent));
            }

            try
            {
                using var result = parent is null
                    ? DependencyContainerFactory.Create(Configure)
                    : parent.CreateScope(Configure);
            }
            catch (AggregateException)
            {
                return (failedContainer!, references);
            }

            throw new InvalidOperationException("The build unexpectedly succeeded.");

            #region Local Functions
            void Configure(IDependencyRegistrar registrar)
            {
                registrar.RegisterScoped<Resource>(factory: (_, ref collector) =>
                {
                    GC.KeepAlive(registrationState);

                    var service = new Resource();
                    var collected = new Resource(() =>
                    {
                        if (rollbackFails)
                        {
                            throw new ArgumentException("Cleanup failed.");
                        }
                    });

                    references.Add("cached service", new WeakReference(service));
                    references.Add("collected entry", new WeakReference(collected));
                    collector.Add(collected);

                    return service;
                });
                registrar.RegisterSingleton<Service>(factory: (container, ref collector) =>
                {
                    failedContainer = container;
                    container.Resolve<Resource>();

                    var collected = new Resource();

                    references.Add("failed factory collection", new WeakReference(collected));
                    collector.Add(collected);

                    throw new ArgumentException("Creation failed.");
                });
            }
            #endregion
        }
        #endregion
    }
}
