// ReSharper disable AccessToModifiedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyInstallerTests
{
    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class Service;

    private sealed class Installer(Action<IDependencyRegistrar> configure) : IDependencyInstaller, IDisposable
    {
        public int InstallCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Install(IDependencyRegistrar registrar)
        {
            InstallCount++;
            configure(registrar);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    [Test]
    public void Installation_ShouldFinishOnceBeforeSharedServicesAreCreated(
        [Values] bool createChild,
        [Values] bool useActionAdapter
    )
    {
        var events = new List<string>();
        using var parent = DependencyContainerFactory.Create(_ => { });
        IDependencyInstaller installer = useActionAdapter
            ? new ActionDependencyInstaller(Configure)
            : new Installer(Configure);
        using var container = createChild
            ? parent.CreateScope(installer)
            : DependencyContainerFactory.Create(installer);

        Assert.That(events, Is.EqualTo(new[] { "install", "installed", "create" }));
        Assert.That(container.Resolve<Resource>(), Is.Not.Null);
        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(parent.Contains<Resource>(), Is.False);

        void Configure(IDependencyRegistrar registrar)
        {
            events.Add("install");
            registrar.RegisterSingleton(factory: () =>
            {
                events.Add("create");
                return new Resource();
            });
            events.Add("installed");
        }
    }

    [Test]
    public void InstallationFailure_ShouldPropagateOriginalErrorBeforeFactoriesRun(
        [Values] bool createChild,
        [Values] bool useActionAdapter
    )
    {
        var failure = new AggregateException(new ArgumentException("Installation failed."));
        var factoryCalls = 0;
        using var parent = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton<Service>());
        var parentService = parent.Resolve<Service>();
        IDependencyInstaller installer = useActionAdapter
            ? new ActionDependencyInstaller(Configure)
            : new Installer(Configure);

        var error = Assert.Throws<AggregateException>(() =>
        {
            using var container = createChild
                ? parent.CreateScope(installer)
                : DependencyContainerFactory.Create(installer);
        });

        Assert.That(error, Is.SameAs(failure));
        Assert.That(factoryCalls, Is.Zero);
        Assert.That(parent.Resolve<Service>(), Is.SameAs(parentService));
        Assert.That(parent.Contains<Resource>(), Is.False);

        void Configure(IDependencyRegistrar registrar)
        {
            registrar.RegisterSingleton(factory: () =>
            {
                factoryCalls++;
                return new Resource();
            });
            throw failure;
        }
    }

    [Test]
    public void ReusedInstaller_ShouldCreateIndependentContainersWithoutTakingInstallerOwnership([Values] bool createChild)
    {
        var registrars = new List<IDependencyRegistrar>();
        var installer = new Installer(registrar =>
        {
            registrars.Add(registrar);
            registrar.RegisterSingleton<Resource>();
        });
        using var parent = DependencyContainerFactory.Create(_ => { });
        var first = createChild ? parent.CreateScope(installer) : DependencyContainerFactory.Create(installer);
        var second = createChild ? parent.CreateScope(installer) : DependencyContainerFactory.Create(installer);
        var firstResource = first.Resolve<Resource>();
        var secondResource = second.Resolve<Resource>();

        Assert.That(installer.InstallCount, Is.EqualTo(2));
        Assert.That(registrars, Has.Count.EqualTo(2));
        Assert.That(registrars[0], Is.Not.SameAs(registrars[1]));
        Assert.That(firstResource, Is.Not.SameAs(secondResource));

        first.Dispose();

        Assert.That(firstResource.DisposeCount, Is.EqualTo(1));
        Assert.That(secondResource.DisposeCount, Is.Zero);
        Assert.That(second.Resolve<Resource>(), Is.SameAs(secondResource));

        second.Dispose();

        Assert.That(secondResource.DisposeCount, Is.EqualTo(1));
        Assert.That(installer.DisposeCount, Is.Zero);
    }

    [Test]
    public void BuildFailure_ShouldCleanCreatedServicesWithoutDisposingInstaller([Values] bool createChild)
    {
        var resource = new Resource();
        var failure = new ArgumentException("Creation failed.");
        var installer = new Installer(registrar =>
        {
            registrar.RegisterSingleton(resource);
            registrar.RegisterSingleton<Service>(factory: (container, ref _) =>
            {
                container.Resolve<Resource>();
                throw failure;
            });
        });
        using var parent = DependencyContainerFactory.Create(_ => { });

        var error = Assert.Throws<AggregateException>(() =>
        {
            using var container = createChild
                ? parent.CreateScope(installer)
                : DependencyContainerFactory.Create(installer);
        });

        Assert.That(error!.InnerExceptions, Is.EqualTo(new[] { failure }));
        Assert.That(resource.DisposeCount, Is.EqualTo(1));
        Assert.That(installer.InstallCount, Is.EqualTo(1));
        Assert.That(installer.DisposeCount, Is.Zero);
        Assert.That(parent.Contains<Resource>(), Is.False);
    }

    [Test]
    public void EmptyScope_ShouldInheritParentWithoutAddingRegistrations([Values] bool useActionOverload)
    {
        using var parent = DependencyContainerFactory.Create(new Installer(registrar => registrar.RegisterSingleton<Resource>()));
        var resource = parent.Resolve<Resource>();
        var child = useActionOverload ? parent.CreateScope(_ => { }) : parent.CreateScope();

        Assert.That(child.Resolve<Resource>(), Is.SameAs(resource));
        Assert.That(child.Contains<Service>(), Is.False);

        child.Dispose();

        Assert.That(resource.DisposeCount, Is.Zero);
        Assert.That(parent.Resolve<Resource>(), Is.SameAs(resource));
    }

    [Test]
    public void DisposedContainer_ShouldRejectScopeBeforeInstallerRuns()
    {
        var installer = new Installer(_ => { });
        var container = DependencyContainerFactory.Create(_ => { });
        container.Dispose();

        Assert.That(() => container.CreateScope(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.CreateScope(installer), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(installer.InstallCount, Is.Zero);
    }

    [Test]
    public void ServiceCreation_ShouldRejectScopeBeforeInstallerRuns([Values] Lifetime lifetime)
    {
        var installer = new Installer(_ => { });
        var attemptScopeCreation = true;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: (resolver, ref _) =>
            {
                if (attemptScopeCreation)
                {
                    var owningContainer = (IDependencyContainer)resolver;

                    Assert.That(() => owningContainer.CreateScope(installer), Throws.InvalidOperationException);
                    Assert.That(installer.InstallCount, Is.Zero);
                }

                return new Resource();
            }, lifetime);
        });
        container.Resolve<Resource>();
        attemptScopeCreation = false;

        using var child = container.CreateScope(installer);

        Assert.That(installer.InstallCount, Is.EqualTo(1));
    }
}
