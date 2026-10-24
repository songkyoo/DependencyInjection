// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerDisposalTests
{
    private sealed class Foo(Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose();
        }
    }

    [Test]
    public void ScopedInstances_ShouldBeDisposed_OnlyByOwningContainer()
    {
        var disposedInRoot = false;
        var disposedInChild = false;

        var key = Key<Foo>.Default;
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, (_, ref _) => new Foo(onDispose: () => disposedInRoot = true), Lifetime.Scoped);
        });
        var child = root.CreateScope(registrar =>
        {
            registrar.Register(key, (_, ref _) => new Foo(onDispose: () => disposedInChild = true), Lifetime.Scoped);
        });

        root.Resolve(key);
        child.Resolve(key);

        child.Dispose();

        Assert.That(disposedInChild, Is.True);
        Assert.That(disposedInRoot, Is.False);

        root.Dispose();

        Assert.That(disposedInRoot, Is.True);
    }

    [Test]
    public void DisposedContainer_ShouldRejectResolutionAndScopeCreation_BeforeConfiguration()
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(factory: () => new Foo(() => { }));
        });
        var configurationCalls = 0;
        var installer = new ActionDependencyInstaller(_ => configurationCalls++);

        container.Dispose();

        Assert.That(container.Resolve<Foo>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(container.Contains<Foo>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.CreateScope(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.CreateScope(_ => configurationCalls++), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.CreateScope(installer), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(container.CreateTransientScope, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(configurationCalls, Is.Zero);
    }
}
