namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerInitializationTests
{
    private sealed class Resource(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    private sealed record Service(Resource Resource);

    [Test]
    public void RootCreation_ShouldPrepareSharedService_WithoutCreatingUnusedTransient(
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var sharedCreations = 0;
        var transientCreations = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: () =>
            {
                sharedCreations++;

                return new Resource(() => { });
            }, lifetime);
            registrar.RegisterTransient(factory: () =>
            {
                transientCreations++;

                return new object();
            });
        });

        Assert.That(sharedCreations, Is.EqualTo(1));
        Assert.That(transientCreations, Is.Zero);
        Assert.That(root.Contains<object>(), Is.True);

        var resource = root.Resolve<Resource>();

        Assert.That(root.Resolve<Resource>(), Is.SameAs(resource));
        Assert.That(sharedCreations, Is.EqualTo(1));
    }

    [Test]
    public void RootSingletonInstance_ShouldBeCleanedEvenWithoutExplicitResolve([Values] bool customCleanup)
    {
        var defaultCleanups = 0;
        var customCleanups = 0;
        var instance = new Resource(() => defaultCleanups++);
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(instance, cleanup: customCleanup ? _ => customCleanups++ : null);
        });

        Assert.That(defaultCleanups, Is.Zero);
        Assert.That(customCleanups, Is.Zero);

        root.Dispose();
        root.Dispose();

        Assert.That(defaultCleanups, Is.EqualTo(customCleanup ? 0 : 1));
        Assert.That(customCleanups, Is.EqualTo(customCleanup ? 1 : 0));
    }

    [Test]
    public void SharedServices_ShouldResolveDependencyFirst_AndReuseItsPreparedInstance(
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime serviceLifetime,
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime dependencyLifetime
    )
    {
        var firstTag = "first";
        var secondTag = "second";
        var events = new List<string>();
        var dependencyCreations = 0;
        var serviceCreations = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Service>(CreateService, serviceLifetime, tag: firstTag);
            registrar.Register<Service>(CreateService, serviceLifetime, tag: secondTag);
            registrar.Register(factory: () =>
            {
                dependencyCreations++;
                events.Add("dependency");

                return new Resource(() => { });
            }, dependencyLifetime);
        });

        Assert.That(dependencyCreations, Is.EqualTo(1));
        Assert.That(serviceCreations, Is.EqualTo(2));
        Assert.That(events, Is.EqualTo(new[] { "dependency", "service", "service" }));
        Assert.That(root.Resolve<Service>(firstTag).Resource, Is.SameAs(root.Resolve<Resource>()));
        Assert.That(root.Resolve<Service>(secondTag).Resource, Is.SameAs(root.Resolve<Resource>()));

        Service CreateService(IDependencyResolver resolver, ref CleanupCollector collector)
        {
            var resource = resolver.Resolve<Resource>();

            serviceCreations++;
            events.Add("service");

            return new Service(resource);
        }
    }

    [Test]
    public void SharedServiceTransientDependencyAndCollectedEntries_ShouldBelongToRoot(
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var cleanups = new List<string>();
        var transientCreations = 0;
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(factory: (_, ref collector) =>
            {
                transientCreations++;
                collector.Add(new object(), _ => cleanups.Add("dependency-collected"));

                return new Resource(() => cleanups.Add("dependency"));
            });
            registrar.Register<Service>(factory: (container, ref collector) =>
            {
                var resource = container.Resolve<Resource>();

                collector.Add(new object(), _ => cleanups.Add("service-collected"));

                return new Service(resource);
            }, lifetime, cleanup: _ => cleanups.Add("service"));
        });

        Assert.That(transientCreations, Is.EqualTo(1));
        Assert.That(cleanups, Is.Empty);

        root.Dispose();
        root.Dispose();

        Assert.That(cleanups, Is.EqualTo(new[]
        {
            "service-collected", "service", "dependency-collected", "dependency"
        }));
    }

    [Test]
    public void Build_ShouldStopAtFirstEscapingFactoryError([Values] bool createChild)
    {
        using var parent = DependencyContainerFactory.Create(_ => { });
        var factoryCalls = 0;
        var failure = new ArgumentException("Creation failed.");
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

        Assert.That(factoryCalls, Is.EqualTo(1));
        Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { failure }));

        void Configure(IDependencyRegistrar registrar)
        {
            foreach (var name in new[] { "first", "second", "third" })
            {
                registrar.RegisterSingleton<object>(factory: () =>
                {
                    factoryCalls++;
                    throw failure;
                }, tag: name);
            }
        }
    }
}
