// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerDisposalTests
{
    private sealed class Foo : IDisposable
    {
        private readonly Action? _onDispose;

        public Foo()
        {
        }

        public Foo(Action? onDispose)
        {
            _onDispose = onDispose;
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            _onDispose?.Invoke();
        }
    }

    [Test]
    public void ScopedDisposable_ShouldBeDisposed_WhenContainerDisposed()
    {
        var disposed = false;
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(
                key: Key.Of<Foo>(),
                factory: (_, ref _) => new Foo(onDispose: () => disposed = true),
                lifeTime: LifeTime.Scoped
            );
        });

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
        var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, (_, ref _) => new Foo(onDispose: () => disposedInRoot = true), LifeTime.Scoped);
        });
        var child = root.CreateScope(registrar =>
        {
            registrar.Register(key, (_, ref _) => new Foo(onDispose: () => disposedInChild = true), LifeTime.Scoped);
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
    public void TransientDisposable_ShouldBeDisposed_WhenContainerDisposed()
    {
        var disposed = false;
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(
                key: Key.Of<Foo>(),
                factory: (_, ref _) => new Foo(onDispose: () => disposed = true),
                lifeTime: LifeTime.Transient
            );
        });

        container.Resolve(Key.Of<Foo>());
        container.Dispose();

        Assert.That(disposed, Is.True);
    }

    [Test]
    public void SingletonDisposable_ShouldBeDisposed_WhenRootContainerDisposed()
    {
        var disposed = false;
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(
                key: Key.Of<Foo>(),
                factory: (_, ref _) => new Foo(onDispose: () => disposed = true),
                lifeTime: LifeTime.Singleton
            );
        });

        container.Resolve(Key.Of<Foo>());
        container.Dispose();

        Assert.That(disposed, Is.True);
    }

    [Test]
    public void Resolve_AfterDisposal_ShouldThrow()
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(Key.Of<string>(), factory: (_, ref _) => "Hello", LifeTime.Singleton);
        });

        container.Dispose();

        Assert.That(() => container.Resolve(Key.Of<string>()), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void CreateScope_AfterDisposal_ShouldThrow_BeforeConfiguration()
    {
        var container = DependencyContainerFactory.Create();

        container.Dispose();

        var configurationCalled = false;

        Assert.That(
            () => container.CreateScope(_ => configurationCalled = true),
            Throws.TypeOf<ObjectDisposedException>()
        );
        Assert.That(configurationCalled, Is.False);
        Assert.That(container.CreateScope, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Contains_AfterDisposal_ShouldThrow()
    {
        var container = DependencyContainerFactory.Create();

        container.Dispose();

        Assert.That(container.Contains<Foo>, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void RegisterSingletonWithEmptyCleanup_ShouldNotDisposeInstance_WhenContainerDisposed()
    {
        var isDisposed = false;
        var foo = new Foo(onDispose: () => isDisposed = true);

        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(foo, cleanup: _ => { });
        });

        container.Resolve(Key.Of<Foo>());
        container.Dispose();

        Assert.That(isDisposed, Is.False);
    }

    [Test]
    public void RegisterSingletonFactoryWithEmptyCleanup_ShouldNotDisposeInstance_WhenContainerDisposed()
    {
        var isDisposed = false;

        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(
                factory: (_, ref _) => new Foo(onDispose: () => isDisposed = true),
                cleanup: _ => { }
            );
        });

        container.Resolve(Key.Of<Foo>());
        container.Dispose();

        Assert.That(isDisposed, Is.False);
    }

    [Test]
    public void RegisterSingletonImplementationWithEmptyCleanup_ShouldNotDisposeInstance_WhenContainerDisposed()
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<IDisposable, Foo>(cleanup: _ => { });
        });
        var foo = (Foo)container.Resolve(Key.Of<IDisposable>());

        container.Dispose();

        Assert.That(foo.IsDisposed, Is.False);
    }
}
