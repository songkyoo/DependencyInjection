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
    public void NullSharedFactoryResult_ShouldFailBuild_CleanCollector_AndRejectServiceOwnership(
        [Values] bool createChild,
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime,
        [Values] bool useDefaultKey
    )
    {
        var key = useDefaultKey ? Key<Service>.Default : new Key<Service>("service");
        using var parent = DependencyContainerFactory.Create(_ => { });
        var collected = new Resource();
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

        Assert.That(validationError.Message, Does.Contain(key.ToString()));

        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(serviceCleanups, Is.Zero);
        Assert.That(failedContainer!.Contains<Service>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer!.Dispose, Throws.Nothing);
        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(parent.Contains<Service>(), Is.False);

        void Configure(IDependencyRegistrar registrar)
        {
            DependencyFactory<Service> factory = (container, ref collector) =>
            {
                failedContainer = (IDependencyContainer)container;
                collector.Add(collected);

                return null!;
            };

            if (lifetime == Lifetime.Scoped)
            {
                registrar.RegisterScoped(key, factory, _ => serviceCleanups++);
            }
            else
            {
                registrar.RegisterSingleton(key, factory, _ => serviceCleanups++);
            }
        }
    }

    [Test]
    public void CycleThroughTransientDependency_ShouldFailBuild_WithResolutionPath(
        [Values] bool createChild,
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        using var parent = DependencyContainerFactory.Create(_ => { });
        IDependencyContainer? failedContainer = null;
        var firstKey = new Key<CycleA>("cycle");
        var secondKey = new Key<CycleB>("cycle");

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
                failedContainer = (IDependencyContainer)container;
                container.Resolve(secondKey);

                return new CycleA();
            }, lifetime);
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
        var parentScopedKey = new Key<Resource>("parent-scoped");
        var parentSingletonKey = new Key<Resource>("parent-singleton");
        var ownedKey = new Key<Resource>("owned");
        var scopedInstances = new Dictionary<IDependencyResolver, Resource>();
        using var parent = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>(parentScopedKey, factory: (container, ref _) =>
            {
                var resource = new Resource();

                scopedInstances.Add(container, resource);

                return resource;
            });
            registrar.RegisterSingleton(parentSingletonKey, factory: () => new Resource());
        });
        using var sibling = parent.CreateScope(_ => { });
        var parentResource = scopedInstances[parent];
        var siblingResource = scopedInstances[sibling];
        var sharedResource = parent.Resolve(parentSingletonKey);
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
        Assert.That(() => failedContainer!.CreateScope(_ => { }), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer!.Dispose, Throws.Nothing);
        Assert.That(cleanups, Has.Count.EqualTo(5));
        Assert.That(parent.Resolve(parentScopedKey), Is.SameAs(parentResource));
        Assert.That(sibling.Resolve(parentScopedKey), Is.SameAs(siblingResource));
        Assert.That(parentResource.DisposeCount, Is.Zero);
        Assert.That(siblingResource.DisposeCount, Is.Zero);
        Assert.That(sharedResource.DisposeCount, Is.Zero);

        if (createChild)
        {
            Assert.That(scopedInstances[failedContainer!].DisposeCount, Is.EqualTo(1));
        }

        using var retry = parent.CreateScope(_ => { });

        Assert.That(retry.Resolve(parentSingletonKey), Is.SameAs(sharedResource));

        #region Local Functions
        void Configure(IDependencyRegistrar registrar)
        {
            registrar.RegisterScoped<Resource>(ownedKey, factory: (_, ref collector) =>
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
                failedContainer = (IDependencyContainer)container;
                container.Resolve(ownedKey);

                if (createChild)
                {
                    container.Resolve(parentScopedKey);
                    container.Resolve(parentSingletonKey);
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
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var parentKey = new Key<Resource>("parent");
        var firstKey = new Key<Resource>("first");
        var secondKey = new Key<object>("second");
        var protectedResource = new Resource();
        using var parent = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(parentKey, protectedResource));
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
        Assert.That(parent.Resolve(parentKey), Is.SameAs(protectedResource));

        void Configure(IDependencyRegistrar registrar)
        {
            registrar.Register(
                key: firstKey,
                factory: () =>
                {
                    creationOrder.Add("first");

                    return sharedResource;
                },
                lifetime,
                cleanup: resource =>
                {
                    cleanupOrder.Add("first");
                    resource.Dispose();
                }
            );
            registrar.Register<object>(
                key: secondKey,
                factory: () =>
                {
                    creationOrder.Add("second");

                    return sharedResource;
                },
                lifetime,
                cleanup: _ => cleanupOrder.Add("second")
            );
            registrar.RegisterSingleton<Service>(
                factory: (container, ref collector) =>
                {
                    container.Resolve(firstKey);
                    container.Resolve(secondKey);

                    if (createChild)
                    {
                        container.Resolve(parentKey);
                        container.Resolve(parentKey);
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
            var parent = createChild ? DependencyContainerFactory.Create(_ => { }) : null;

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
                    failedContainer = (IDependencyContainer)container;
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
