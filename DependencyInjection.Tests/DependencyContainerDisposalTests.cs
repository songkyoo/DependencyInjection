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
    public void Resolve_AfterDisposal_ShouldThrow()
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(Key<string>.Default, factory: (_, ref _) => "Hello", Lifetime.Singleton);
        });

        container.Dispose();

        Assert.That(() => container.Resolve(Key<string>.Default), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void CreateScope_AfterDisposal_ShouldThrow_BeforeConfiguration()
    {
        var container = DependencyContainerFactory.Create(_ => { });

        container.Dispose();

        var configurationCalled = false;

        Assert.That(
            () => container.CreateScope(_ => configurationCalled = true),
            Throws.TypeOf<ObjectDisposedException>()
        );
        Assert.That(configurationCalled, Is.False);
        Assert.That(() => container.CreateScope(), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Contains_AfterDisposal_ShouldThrow()
    {
        var container = DependencyContainerFactory.Create(_ => { });

        container.Dispose();

        Assert.That(container.Contains<Foo>, Throws.TypeOf<ObjectDisposedException>());
    }
}
