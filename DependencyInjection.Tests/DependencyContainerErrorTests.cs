namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerErrorTests
{
    #pragma warning disable CS9113
    private sealed class A(B b);

    private sealed class B(C c);

    private sealed class C(A a);
    #pragma warning restore CS9113

    [Test]
    public void NullFactoryResult_ShouldThrow_WithRegistrationKey_AndAllowRetry(
        [Values(LifeTime.Transient, LifeTime.Scoped, LifeTime.Singleton)] LifeTime lifeTime,
        [Values(null, "service-tag")] string? tag,
        [Values(false, true)] bool resolveFromChild
    )
    {
        var key = Key.Of<string>(tag);
        var returnNull = true;
        var builder = new DependencyContainerBuilder();

        // ReSharper disable once AccessToModifiedClosure
        builder.Register(key, factory: _ => returnNull ? null! : "Hello", lifeTime);

        using var root = builder.Build();
        using var child = root.CreateScope();
        var container = resolveFromChild ? child : root;

        var ex = Assert.Throws<InvalidOperationException>(() => container.Resolve(key));

        Assert.That(ex!.Message, Does.Contain("Factory returned null"));
        Assert.That(ex.Message, Does.Contain(key.ToString()));

        returnNull = false;

        Assert.That(container.Resolve(key), Is.EqualTo("Hello"));
    }

    [Test]
    public void SingletonNullFactoryResultWithEmptyCleanup_ShouldThrow_WithRegistrationKey()
    {
        var key = Key.Of<string>("external-service");
        var builder = new DependencyContainerBuilder();

        builder.RegisterSingleton(key, factory: _ => (string)null!, cleanup: _ => { });

        using var container = builder.Build();
        var ex = Assert.Throws<InvalidOperationException>(() => container.Resolve(key));

        Assert.That(ex!.Message, Does.Contain("Factory returned null"));
        Assert.That(ex.Message, Does.Contain(key.ToString()));
    }

    [Test]
    public void CyclicDependency_ShouldThrow_WithResolutionPath()
    {
        var builder = new DependencyContainerBuilder();
        builder.Register(Key.Of<A>(), factory: resolver => new A(b: resolver.Resolve<B>()), LifeTime.Transient);
        builder.Register(Key.Of<B>(), factory: resolver => new B(c: resolver.Resolve<C>()), LifeTime.Transient);
        builder.Register(Key.Of<C>(), factory: resolver => new C(a: resolver.Resolve<A>()), LifeTime.Transient);

        var container = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => container.Resolve(Key.Of<A>()));

        Assert.That(ex!.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+A"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+B"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+C"));
    }
}
