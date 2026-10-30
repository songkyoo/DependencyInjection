// ReSharper disable AccessToModifiedClosure
// ReSharper disable DisposeOnUsingVariable
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerChildInitializationTests
{
    private sealed class Resource(Action? onDispose = null) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            onDispose?.Invoke();
        }
    }

    private sealed record Service(Resource Resource);

    private sealed record Consumer(Service Service);

    [Test]
    public void ChildAndGrandchild_ShouldPrepareScopedInstances_AndShareInheritedSingletons()
    {
        var rootTag = "root";
        var childTag = "child";
        var scopedInstances = new Dictionary<IDependencyResolver, Resource>();
        var rootSingletonCreations = 0;
        var childSingletonCreations = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>(factory: (container, ref _) =>
            {
                var resource = new Resource();

                scopedInstances.Add(container, resource);

                return resource;
            });
            registrar.RegisterSingleton<object>(factory: () =>
            {
                rootSingletonCreations++;

                return new object();
            }, tag: rootTag);
        });
        using var child = root.CreateChild(registrar =>
        {
            registrar.RegisterSingleton<object>(factory: () =>
            {
                childSingletonCreations++;

                return new object();
            }, tag: childTag);
        });

        Assert.That(scopedInstances, Has.Count.EqualTo(2));
        Assert.That(childSingletonCreations, Is.EqualTo(1));

        using var sibling = root.CreateChild();
        using var grandchild = child.CreateChild();

        Assert.That(scopedInstances, Has.Count.EqualTo(4));
        Assert.That(scopedInstances.Values.Distinct().Count(), Is.EqualTo(4));
        Assert.That(rootSingletonCreations, Is.EqualTo(1));
        Assert.That(childSingletonCreations, Is.EqualTo(1));

        foreach (var container in new[] { root, child, sibling, grandchild })
        {
            Assert.That(container.Resolve<Resource>(), Is.SameAs(scopedInstances[container]));
            Assert.That(container.Resolve<object>(rootTag), Is.SameAs(root.Resolve<object>(rootTag)));
        }

        Assert.That(grandchild.Resolve<object>(childTag), Is.SameAs(child.Resolve<object>(childTag)));
        Assert.That(sibling.Contains<object>(childTag), Is.False);
    }

    [Test]
    public void InheritedScopedFactory_ShouldResolveCachedParentInstance_AndKeepCleanupOwnership(
        [Values] bool useDefaultTag
    )
    {
        var tag = useDefaultTag ? null : "resource";
        IDependencyContainer? parent = null;
        Resource? parentResourceResolvedDuringCreation = null;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>(factory: (_, ref _) =>
            {
                parentResourceResolvedDuringCreation = parent?.Resolve<Resource>(tag);

                return new Resource();
            }, tag: tag);
        });
        parent = root;

        var rootResource = root.Resolve<Resource>(tag);

        using var child = root.CreateChild();
        var childResource = child.Resolve<Resource>(tag);

        Assert.That(parentResourceResolvedDuringCreation, Is.SameAs(rootResource));
        Assert.That(childResource, Is.Not.SameAs(rootResource));
        Assert.That(child.Resolve<Resource>(tag), Is.SameAs(childResource));
        Assert.That(root.Resolve<Resource>(tag), Is.SameAs(rootResource));

        child.Dispose();

        Assert.That(childResource.IsDisposed, Is.True);
        Assert.That(rootResource.IsDisposed, Is.False);
        Assert.That(root.Resolve<Resource>(tag), Is.SameAs(rootResource));

        root.Dispose();

        Assert.That(rootResource.IsDisposed, Is.True);
    }

    [Test]
    public void InheritedScopedCycle_ShouldFailChildCreation_AndAllowRetry([Values] bool useDefaultTag)
    {
        var tag = useDefaultTag ? null : "service";
        var introduceCycle = false;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>(factory: (resolver, ref _) =>
            {
                if (introduceCycle)
                {
                    resolver.Resolve<Service>(tag);
                }

                return new Resource();
            }, tag: tag);
            registrar.RegisterScoped<Service>(factory: (resolver, ref _) =>
            {
                return new Service(resolver.Resolve<Resource>(tag));
            }, tag: tag);
        });
        var rootService = root.Resolve<Service>(tag);

        introduceCycle = true;

        var error = Assert.Throws<AggregateException>(() => root.CreateChild());
        var creationError = error!.InnerExceptions.Single();

        Assert.That(creationError, Is.TypeOf<InvalidOperationException>());
        Assert.That(creationError.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(creationError.Message, Does.Contain(typeof(Resource).ToString()));
        Assert.That(creationError.Message, Does.Contain(typeof(Service).ToString()));
        Assert.That(root.Resolve<Service>(tag), Is.SameAs(rootService));
        Assert.That(rootService.Resource.IsDisposed, Is.False);

        introduceCycle = false;

        using var retry = root.CreateChild();
        var childService = retry.Resolve<Service>(tag);

        Assert.That(childService, Is.Not.SameAs(rootService));
        Assert.That(childService.Resource, Is.Not.SameAs(rootService.Resource));
        Assert.That(childService.Resource.IsDisposed, Is.False);
    }

    [Test]
    public void InheritedScopedInitializationFailure_ShouldRollbackOnlyChild_AndPreserveErrorTree(
        [Values] bool useActionOverload,
        [Values] bool cleanupFails
    )
    {
        var failInitialization = false;
        var creationFailure = new ArgumentException("Scoped creation failed.");
        var cleanupFailure = new ArgumentException("Scoped cleanup failed.");
        var singletonTag = "singleton";
        var scopedResources = new Dictionary<IDependencyResolver, Resource>();
        IDependencyContainer? failedChild = null;
        using var parent = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Resource>(factory: () => new Resource(), tag: singletonTag);
            registrar.RegisterScoped<Resource>(factory: (resolver, ref _) =>
            {
                var resource = new Resource(() =>
                {
                    if (cleanupFails && ReferenceEquals(resolver, failedChild))
                    {
                        throw cleanupFailure;
                    }
                });
                scopedResources.Add(resolver, resource);

                return resource;
            });
            registrar.RegisterScoped<Service>(factory: (resolver, ref _) =>
            {
                var resource = resolver.Resolve<Resource>();

                if (failInitialization)
                {
                    failedChild = (IDependencyContainer)resolver;
                    throw creationFailure;
                }

                return new Service(resource);
            });
        });
        using var sibling = parent.CreateChild();
        var parentService = parent.Resolve<Service>();
        var siblingService = sibling.Resolve<Service>();
        var singleton = parent.Resolve<Resource>(singletonTag);
        failInitialization = true;

        var error = Assert.Throws<AggregateException>(() =>
        {
            using var child = useActionOverload ? parent.CreateChild(_ => { }) : parent.CreateChild();
        });

        Assert.That(error!.InnerExceptions, Has.Count.EqualTo(cleanupFails ? 2 : 1));
        Assert.That(error.InnerExceptions[0], Is.SameAs(creationFailure));

        if (cleanupFails)
        {
            var rollbackError = error.InnerExceptions[1] as AggregateException;

            Assert.That(rollbackError, Is.Not.Null);
            Assert.That(rollbackError!.InnerExceptions, Is.EqualTo(new[] { cleanupFailure }));
        }

        Assert.That(scopedResources[failedChild!].IsDisposed, Is.True);
        Assert.That(() => failedChild!.Contains<Service>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => failedChild!.CreateChild(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(failedChild!.Dispose, Throws.Nothing);
        Assert.That(parent.Resolve<Service>(), Is.SameAs(parentService));
        Assert.That(sibling.Resolve<Service>(), Is.SameAs(siblingService));
        Assert.That(parentService.Resource.IsDisposed, Is.False);
        Assert.That(siblingService.Resource.IsDisposed, Is.False);
        Assert.That(singleton.IsDisposed, Is.False);

        failInitialization = false;

        using var retry = parent.CreateChild();

        Assert.That(retry.Resolve<Service>().Resource, Is.SameAs(scopedResources[retry]));
        Assert.That(retry.Resolve<Service>(), Is.Not.SameAs(parentService));
        Assert.That(retry.Resolve<Resource>(singletonTag), Is.SameAs(singleton));
    }

    [Test]
    public void NearestRegistration_ShouldControlInitialization_EvenWhenTransientMasksSharedParent(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime parentLifetime,
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime childLifetime,
        [Values] bool useDefaultTag
    )
    {
        var parentCreations = 0;
        var childCreations = 0;
        var tag = useDefaultTag ? null : "service";
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Resource>(factory: () =>
                {
                    parentCreations++;

                    return new Resource();
                }, parentLifetime, tag: tag);
        });
        using var child = root.CreateChild(registrar =>
        {
            registrar.Register<Resource>(factory: () =>
                {
                    childCreations++;

                    return new Resource();
                }, childLifetime, tag: tag);
        });

        Assert.That(childCreations, Is.EqualTo(childLifetime == Lifetime.Transient ? 0 : 1));

        using var grandchild = child.CreateChild();

        var expectedChildCreations = childLifetime switch
        {
            Lifetime.Transient => 0,
            Lifetime.Scoped => 2,
            _ => 1
        };

        Assert.That(childCreations, Is.EqualTo(expectedChildCreations));
        Assert.That(parentCreations, Is.EqualTo(parentLifetime == Lifetime.Transient ? 0 : 1));

        var childResource = child.Resolve<Resource>(tag);
        var grandchildResource = grandchild.Resolve<Resource>(tag);

        Assert.That(grandchildResource, childLifetime == Lifetime.Singleton ? Is.SameAs(childResource) : Is.Not.SameAs(childResource));
        Assert.That(childCreations, Is.EqualTo(childLifetime == Lifetime.Transient ? 2 : expectedChildCreations));
        Assert.That(parentCreations, Is.EqualTo(parentLifetime == Lifetime.Transient ? 0 : 1));
    }

    [Test]
    public void InheritedScopedFactory_ShouldUseChildOverrides_AndOwnTransientDependenciesInChild()
    {
        var transientResources = new List<Resource>();
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(factory: () => new Resource());
            registrar.RegisterScoped<Service>(factory: (container, ref _) => new Service(container.Resolve<Resource>()));
        });
        using var child = root.CreateChild(registrar =>
        {
            registrar.RegisterTransient(factory: () =>
            {
                var resource = new Resource();

                transientResources.Add(resource);

                return resource;
            });
            registrar.RegisterSingleton<Consumer>(factory: (container, ref _) =>
            {
                return new Consumer(container.Resolve<Service>());
            });
        });

        Assert.That(transientResources, Has.Count.EqualTo(1));

        var childResource = transientResources[0];

        Assert.That(child.Resolve<Service>().Resource, Is.SameAs(childResource));
        Assert.That(child.Resolve<Consumer>().Service, Is.SameAs(child.Resolve<Service>()));

        using var grandchild = child.CreateChild();

        Assert.That(transientResources, Has.Count.EqualTo(2));
        Assert.That(grandchild.Resolve<Service>().Resource, Is.SameAs(transientResources[1]));
        Assert.That(grandchild.Resolve<Consumer>(), Is.SameAs(child.Resolve<Consumer>()));

        grandchild.Dispose();

        Assert.That(transientResources[1].IsDisposed, Is.True);
        Assert.That(childResource.IsDisposed, Is.False);
        Assert.That(root.Resolve<Resource>().IsDisposed, Is.False);

        child.Dispose();

        Assert.That(childResource.IsDisposed, Is.True);
        Assert.That(root.Resolve<Resource>().IsDisposed, Is.False);
    }

    [Test]
    public void InheritedScopedNullFactoryResult_ShouldFailChildCreation_AndPreserveParent(
        [Values] bool useDefaultTag
    )
    {
        var tag = useDefaultTag ? null : "service";
        var failCreation = false;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<string>(factory: (_, ref _) => failCreation ? null! : "Hello", tag: tag);
        });

        failCreation = true;

        var exception = Assert.Throws<AggregateException>(() => root.CreateChild());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(1));
        Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain(typeof(string).ToString()));
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain(tag?.ToString() ?? "null"));
        Assert.That(root.Resolve<string>(tag), Is.EqualTo("Hello"));

        failCreation = false;

        using var child = root.CreateChild();

        Assert.That(child.Resolve<string>(tag), Is.EqualTo("Hello"));
    }
}
