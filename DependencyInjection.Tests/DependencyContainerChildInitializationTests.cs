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
        var rootKey = new Key<object>("root");
        var childKey = new Key<object>("child");
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
            registrar.RegisterSingleton(rootKey, factory: () =>
            {
                rootSingletonCreations++;

                return new object();
            });
        });
        using var child = root.CreateScope(registrar =>
        {
            registrar.RegisterSingleton(childKey, factory: () =>
            {
                childSingletonCreations++;

                return new object();
            });
        });

        Assert.That(scopedInstances, Has.Count.EqualTo(2));
        Assert.That(childSingletonCreations, Is.EqualTo(1));

        using var sibling = root.CreateScope(_ => { });
        using var grandchild = child.CreateScope(_ => { });

        Assert.That(scopedInstances, Has.Count.EqualTo(4));
        Assert.That(scopedInstances.Values.Distinct().Count(), Is.EqualTo(4));
        Assert.That(rootSingletonCreations, Is.EqualTo(1));
        Assert.That(childSingletonCreations, Is.EqualTo(1));

        foreach (var container in new[] { root, child, sibling, grandchild })
        {
            Assert.That(container.Resolve<Resource>(), Is.SameAs(scopedInstances[container]));
            Assert.That(container.Resolve(rootKey), Is.SameAs(root.Resolve(rootKey)));
        }

        Assert.That(grandchild.Resolve(childKey), Is.SameAs(child.Resolve(childKey)));
        Assert.That(sibling.Contains(childKey), Is.False);
    }

    [Test]
    public void NearestRegistration_ShouldControlInitialization_EvenWhenTransientMasksSharedParent(
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime parentLifetime,
        [Values(Lifetime.Transient, Lifetime.Scoped, Lifetime.Singleton)] Lifetime childLifetime,
        [Values] bool useDefaultKey
    )
    {
        var parentCreations = 0;
        var childCreations = 0;
        var key = useDefaultKey ? Key<Resource>.Default : new Key<Resource>("service");
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, factory: () =>
            {
                parentCreations++;

                return new Resource();
            }, parentLifetime);
        });
        using var child = root.CreateScope(registrar =>
        {
            registrar.Register(key, factory: () =>
            {
                childCreations++;

                return new Resource();
            }, childLifetime);
        });

        Assert.That(childCreations, Is.EqualTo(childLifetime == Lifetime.Transient ? 0 : 1));

        using var grandchild = child.CreateScope(_ => { });

        var expectedChildCreations = childLifetime switch
        {
            Lifetime.Transient => 0,
            Lifetime.Scoped => 2,
            _ => 1
        };

        Assert.That(childCreations, Is.EqualTo(expectedChildCreations));
        Assert.That(parentCreations, Is.EqualTo(parentLifetime == Lifetime.Transient ? 0 : 1));

        var childResource = child.Resolve(key);
        var grandchildResource = grandchild.Resolve(key);

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
        using var child = root.CreateScope(registrar =>
        {
            registrar.RegisterTransient(factory: () =>
            {
                var resource = new Resource();

                transientResources.Add(resource);

                return resource;
            });
            registrar.RegisterSingleton<Consumer>(factory: (container, ref _) => new Consumer(container.Resolve<Service>()));
        });

        Assert.That(transientResources, Has.Count.EqualTo(1));

        var childResource = transientResources[0];

        Assert.That(child.Resolve<Service>().Resource, Is.SameAs(childResource));
        Assert.That(child.Resolve<Consumer>().Service, Is.SameAs(child.Resolve<Service>()));

        using var grandchild = child.CreateScope(_ => { });

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
}
