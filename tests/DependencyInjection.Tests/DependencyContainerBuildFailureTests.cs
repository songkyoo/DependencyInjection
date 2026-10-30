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
        [Values] bool useDefaultTag
    )
    {
        var tag = useDefaultTag ? null : "service";
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

        Assert.That(validationError.Message, Does.Contain(tag?.ToString() ?? "null"));

        Assert.That(collected.DisposeCount, Is.EqualTo(1));
        Assert.That(serviceCleanups, Is.Zero);
        Assert.That(() => failedContainer!.Contains<Service>(), Throws.TypeOf<ObjectDisposedException>());
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
                registrar.RegisterScoped<Service>(factory, cleanup: _ => serviceCleanups++, tag: tag);
            }
            else
            {
                registrar.RegisterSingleton<Service>(factory, cleanup: _ => serviceCleanups++, tag: tag);
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
        var firstTag = "cycle";
        var secondTag = "cycle";

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
        Assert.That(cycleError.Message, Does.Contain(typeof(CycleA).ToString()));
        Assert.That(cycleError.Message, Does.Contain(typeof(CycleB).ToString()));
        Assert.That(cycleError.Message, Does.Contain(firstTag));
        Assert.That(() => failedContainer!.Contains<CycleA>(), Throws.TypeOf<ObjectDisposedException>());

        #region Local Functions
        void Configure(IDependencyRegistrar registrar)
        {
            registrar.Register<CycleA>(factory: (container, ref _) =>
                {
                    failedContainer = (IDependencyContainer)container;
                    container.Resolve<CycleB>(secondTag);

                    return new CycleA();
                }, lifetime, tag: firstTag);
            registrar.RegisterTransient<CycleB>(factory: (container, ref _) =>
            {
                container.Resolve<CycleA>(firstTag);

                return new CycleB();
            }, tag: secondTag);
        }
        #endregion
    }

    [Test]
    public void FailedBuild_ShouldAttemptEveryCleanup_AndPreserveErrorTreeAndOtherScopes(
        [Values] bool createChild,
        [Values] bool collectorCleanupFails,
        [Values] bool rollbackFails
    )
    {
        var parentScopedTag = "parent-scoped";
        var parentSingletonTag = "parent-singleton";
        var ownedTag = "owned";
        var scopedInstances = new Dictionary<IDependencyResolver, Resource>();
        using var parent = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>(factory: (container, ref _) =>
                {
                    var resource = new Resource();

                    scopedInstances.Add(container, resource);

                    return resource;
                }, tag: parentScopedTag);
            registrar.RegisterSingleton<Resource>(factory: () => new Resource(), tag: parentSingletonTag);
        });
        using var sibling = parent.CreateScope();
        var parentResource = scopedInstances[parent];
        var siblingResource = scopedInstances[sibling];
        var sharedResource = parent.Resolve<Resource>(parentSingletonTag);
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
        Assert.That(() => failedContainer!.Contains<Service>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => failedContainer!.Resolve<Service>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => failedContainer!.CreateScope(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedContainer!.Dispose, Throws.Nothing);
        Assert.That(cleanups, Has.Count.EqualTo(5));
        Assert.That(parent.Resolve<Resource>(parentScopedTag), Is.SameAs(parentResource));
        Assert.That(sibling.Resolve<Resource>(parentScopedTag), Is.SameAs(siblingResource));
        Assert.That(parentResource.DisposeCount, Is.Zero);
        Assert.That(siblingResource.DisposeCount, Is.Zero);
        Assert.That(sharedResource.DisposeCount, Is.Zero);

        if (createChild)
        {
            Assert.That(scopedInstances[failedContainer!].DisposeCount, Is.EqualTo(1));
        }

        using var retry = parent.CreateScope();

        Assert.That(retry.Resolve<Resource>(parentSingletonTag), Is.SameAs(sharedResource));

        #region Local Functions
        void Configure(IDependencyRegistrar registrar)
        {
            registrar.RegisterScoped<Resource>(factory: (_, ref collector) =>
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
            }, tag: ownedTag);
            registrar.RegisterSingleton<Service>(factory: (container, ref collector) =>
            {
                failedContainer = (IDependencyContainer)container;
                container.Resolve<Resource>(ownedTag);

                if (createChild)
                {
                    container.Resolve<Resource>(parentScopedTag);
                    container.Resolve<Resource>(parentSingletonTag);
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
        var parentTag = "parent";
        var firstTag = "first";
        var secondTag = "second";
        var protectedResource = new Resource();
        using var parent = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Resource>(protectedResource, tag: parentTag);
        });
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
        Assert.That(parent.Resolve<Resource>(parentTag), Is.SameAs(protectedResource));

        void Configure(IDependencyRegistrar registrar)
        {
            registrar.Register<Resource>(factory: () =>
                {
                    creationOrder.Add("first");

                    return sharedResource;
                }, lifetime, cleanup: resource =>
                {
                    cleanupOrder.Add("first");
                    resource.Dispose();
                }, tag: firstTag);
            registrar.Register<object>(factory: () =>
                {
                    creationOrder.Add("second");

                    return sharedResource;
                }, lifetime, cleanup: _ => cleanupOrder.Add("second"), tag: secondTag);
            registrar.RegisterSingleton<Service>(
                factory: (container, ref collector) =>
                {
                    container.Resolve<Resource>(firstTag);
                    container.Resolve<object>(secondTag);

                    if (createChild)
                    {
                        container.Resolve<Resource>(parentTag);
                        container.Resolve<Resource>(parentTag);
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

        Assert.That(() => failedContainer.Contains<Resource>(), Throws.TypeOf<ObjectDisposedException>());
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
