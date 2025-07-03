namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerDisposalTests
{
    private sealed class Foo(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    [Test]
    public void ScopedDisposable_ShouldBeDisposed_WhenContainerDisposed()
    {
        var disposed = false;
        var builder = new DependencyContainerBuilder();
        builder.Register(
            Key.Of<Foo>(),
            factory: _ => new Foo(onDispose: () => disposed = true),
            LifeTime.Scoped
        );

        var container = builder.Build();
        container.Resolve(Key.Of<Foo>());
        container.Dispose();

        Assert.That(disposed, Is.True);
    }

    [Test]
    public void ScopedInstances_ShouldBeDisposed_OnlyByOwningContainer()
    {
        var disposedInRoot = false;
        var disposedInChild = false;

        var key = Key.Of<Foo>();

        var rootBuilder = new DependencyContainerBuilder();
        rootBuilder.Register(key, _ => new Foo(onDispose: () => disposedInRoot = true), LifeTime.Scoped);

        var root = rootBuilder.Build();

        var childBuilder = new DependencyContainerBuilder();
        childBuilder.Register(key, _ => new Foo(onDispose: () => disposedInChild = true), LifeTime.Scoped);

        var child = childBuilder.Build(root);

        root.Resolve(key);
        child.Resolve(key);

        child.Dispose();

        Assert.That(disposedInChild, Is.True);
        Assert.That(disposedInRoot, Is.False);

        root.Dispose();

        Assert.That(disposedInRoot, Is.True);
    }

    [Test]
    public void TransientDisposable_ShouldNotBeTrackedOrDisposed()
    {
        var disposed = false;
        var builder = new DependencyContainerBuilder();
        builder.Register(
            Key.Of<Foo>(),
            factory: _ => new Foo(onDispose: () => disposed = true),
            LifeTime.Transient
        );

        var container = builder.Build();
        container.Resolve(Key.Of<Foo>());
        container.Dispose();

        Assert.That(disposed, Is.False);
    }

    [Test]
    public void SingletonDisposable_ShouldBeDisposed_WhenRootContainerDisposed()
    {
        var disposed = false;
        var builder = new DependencyContainerBuilder();
        builder.Register(
            Key.Of<Foo>(),
            factory: _ => new Foo(onDispose: () => disposed = true),
            LifeTime.Singleton
        );

        var container = builder.Build();
        container.Resolve(Key.Of<Foo>());
        container.Dispose();

        Assert.That(disposed, Is.True);
    }

    [Test]
    public void Resolve_AfterDisposal_ShouldThrow()
    {
        var builder = new DependencyContainerBuilder();
        builder.Register(Key.Of<string>(), factory: _ => "Hello", LifeTime.Singleton);

        var container = builder.Build();
        container.Dispose();

        Assert.That(() => container.Resolve(Key.Of<string>()), Throws.TypeOf<ObjectDisposedException>());
    }
}
