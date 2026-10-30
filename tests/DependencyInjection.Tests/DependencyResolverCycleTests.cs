// ReSharper disable AccessToModifiedClosure
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyResolverCycleTests
{
    private sealed record A(B Dependency);

    private sealed record B(C Dependency);

    private sealed class C;

    private sealed class Service;

    private sealed class OtherService;

    private sealed record ValueTag(string Value);

    [Test]
    public void CyclicDependency_ShouldReportResolutionPathAndAllowRetry([Values] bool useLocalScope)
    {
        var cycle = true;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<A>(factory: (resolver, ref _) => new A(resolver.Resolve<B>()));
            registrar.RegisterTransient<B>(factory: (resolver, ref _) => new B(resolver.Resolve<C>()));
            registrar.RegisterTransient<C>(factory: (resolver, ref _) =>
            {
                if (cycle)
                {
                    resolver.Resolve<A>();
                }

                return new C();
            });
        });
        using var scope = container.CreateLocalScope();
        IDependencyResolver resolver = useLocalScope ? scope : container;

        var error = Assert.Throws<InvalidOperationException>(() => resolver.Resolve<A>());

        Assert.That(error!.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(error.Message, Does.Contain(typeof(A).ToString()));
        Assert.That(error.Message, Does.Contain(typeof(B).ToString()));
        Assert.That(error.Message, Does.Contain(typeof(C).ToString()));

        cycle = false;

        Assert.That(resolver.Resolve<A>().Dependency.Dependency, Is.TypeOf<C>());
    }

    [Test]
    public void FailedFactoryCleanup_ShouldRejectRecursiveCreation_AndAllowRetry([Values] bool useLocalScope)
    {
        var failCreation = true;
        var factoryCalls = 0;
        var creationFailure = new ArgumentException("Service creation failed.");
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Service>(factory: (resolver, ref collector) =>
            {
                factoryCalls++;

                if (failCreation)
                {
                    collector.Add(new object(), _ =>
                    {
                        failCreation = false;
                        resolver.Resolve<Service>();
                    });

                    throw creationFailure;
                }

                return new Service();
            });
        });
        using var scope = container.CreateLocalScope();
        IDependencyResolver resolver = useLocalScope ? scope : container;

        var error = Assert.Throws<AggregateException>(() => resolver.Resolve<Service>());

        Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(error.InnerExceptions[0], Is.SameAs(creationFailure));
        Assert.That(error.InnerExceptions[1], Is.TypeOf<InvalidOperationException>());
        Assert.That(error.InnerExceptions[1].Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(factoryCalls, Is.EqualTo(1));

        var service = resolver.Resolve<Service>();

        Assert.That(service, Is.TypeOf<Service>());
        Assert.That(factoryCalls, Is.EqualTo(2));
    }

    [Test]
    public void DifferentDependencyTags_ShouldNotBeDetectedAsCycle()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Service>(
                factory: (resolver, ref _) =>
                {
                    resolver.Resolve<Service>("second");

                    return new Service();
                },
                tag: "first"
            );
            registrar.RegisterTransient<Service>(factory: () => new Service(), tag: "second");
        });

        Assert.That(() => container.Resolve<Service>("first"), Throws.Nothing);
    }

    [Test]
    public void SameTagForDifferentDependencyTypes_ShouldNotBeDetectedAsCycle()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Service>(
                factory: (resolver, ref _) =>
                {
                    resolver.Resolve<OtherService>("service");

                    return new Service();
                },
                tag: "service"
            );
            registrar.RegisterTransient<OtherService>(factory: () => new OtherService(), tag: "service");
        });

        Assert.That(() => container.Resolve<Service>("service"), Throws.Nothing);
    }

    [Test]
    public void RepeatedEqualTag_ShouldBeDetectedAsCycle()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Service>(
                factory: (resolver, ref _) => resolver.Resolve<Service>(new ValueTag("service")),
                tag: new ValueTag("service")
            );
        });
        using var scope = container.CreateLocalScope();

        var error = Assert.Throws<InvalidOperationException>(() => scope.Resolve<Service>(new ValueTag("service")));

        Assert.That(error!.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(error.Message, Does.Contain(typeof(Service).ToString()));
        Assert.That(error.Message, Does.Contain("service"));
    }
}
