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
        [Values] bool useActionOverload
    )
    {
        var events = new List<string>();
        using var parent = DependencyContainerFactory.Create(_ => { });
        using var container = (createChild, useActionOverload) switch
        {
            (true, true) => parent.CreateScope(Configure),
            (true, false) => parent.CreateScope(new Installer(Configure)),
            (false, true) => DependencyContainerFactory.Create(Configure),
            (false, false) => DependencyContainerFactory.Create(new Installer(Configure))
        };

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
        [Values] bool useActionOverload
    )
    {
        var failure = new AggregateException(new ArgumentException("Installation failed."));
        var factoryCalls = 0;
        using var parent = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton<Service>(factory: () => new Service()));
        var parentService = parent.Resolve<Service>();
        var error = Assert.Throws<AggregateException>(() =>
        {
            using var container = (createChild, useActionOverload) switch
            {
                (true, true) => parent.CreateScope(Configure),
                (true, false) => parent.CreateScope(new Installer(Configure)),
                (false, true) => DependencyContainerFactory.Create(Configure),
                (false, false) => DependencyContainerFactory.Create(new Installer(Configure))
            };
        });

        Assert.That(error, Is.SameAs(failure));
        Assert.That(factoryCalls, Is.Zero);
        Assert.That(parent.Resolve<Service>(), Is.SameAs(parentService));
        Assert.That(parent.Contains<Resource>(), Is.False);

        #region Local Functions
        void Configure(IDependencyRegistrar registrar)
        {
            registrar.RegisterSingleton(factory: () =>
            {
                factoryCalls++;

                return new Resource();
            });

            throw failure;
        }
        #endregion
    }

    [Test]
    public void ReusedInstallerAndTag_ShouldCreateIndependentContainersWithoutTakingInstallerOwnership(
        [Values] bool createChild,
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime lifetime
    )
    {
        var tag = "shared";
        var registrars = new List<IDependencyRegistrar>();
        var installer = new Installer(registrar =>
        {
            registrars.Add(registrar);
            registrar.Register<Resource>(factory: () => new Resource(), lifetime, tag: tag);
        });
        using var parent = DependencyContainerFactory.Create(_ => { });
        var first = createChild ? parent.CreateScope(installer) : DependencyContainerFactory.Create(installer);
        var second = createChild ? parent.CreateScope(installer) : DependencyContainerFactory.Create(installer);
        var firstResource = first.Resolve<Resource>(tag);
        var secondResource = second.Resolve<Resource>(tag);

        Assert.That(installer.InstallCount, Is.EqualTo(2));
        Assert.That(registrars, Has.Count.EqualTo(2));
        Assert.That(registrars[0], Is.Not.SameAs(registrars[1]));
        Assert.That(firstResource, Is.Not.SameAs(secondResource));
        Assert.That(first.Resolve<Resource>(tag), Is.SameAs(firstResource));

        first.Dispose();

        Assert.That(firstResource.DisposeCount, Is.EqualTo(1));
        Assert.That(secondResource.DisposeCount, Is.Zero);
        Assert.That(second.Resolve<Resource>(tag), Is.SameAs(secondResource));

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
        using var parent = DependencyContainerFactory.Create(new Installer(registrar => registrar.RegisterSingleton<Resource>(factory: () => new Resource())));
        var resource = parent.Resolve<Resource>();
        var child = useActionOverload ? parent.CreateScope(_ => { }) : parent.CreateScope();

        Assert.That(child.Resolve<Resource>(), Is.SameAs(resource));
        Assert.That(child.Contains<Service>(), Is.False);

        child.Dispose();

        Assert.That(resource.DisposeCount, Is.Zero);
        Assert.That(parent.Resolve<Resource>(), Is.SameAs(resource));
    }
}
