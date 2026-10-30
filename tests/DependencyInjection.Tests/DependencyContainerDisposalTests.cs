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

        object? tag = null;
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Foo>((_, ref _) => new Foo(onDispose: () => disposedInRoot = true), Lifetime.Scoped, tag: tag);
        });
        var child = root.CreateChild(registrar =>
        {
            registrar.Register<Foo>((_, ref _) => new Foo(onDispose: () => disposedInChild = true), Lifetime.Scoped, tag: tag);
        });

        root.Resolve<Foo>(tag);
        child.Resolve<Foo>(tag);

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

        Assert.That(() => container.Resolve<Foo>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.Contains<Foo>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.CreateChild(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.CreateChild(_ => configurationCalls++), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => container.CreateChild(installer), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(container.CreateLocalScope, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(configurationCalls, Is.Zero);
    }
}
