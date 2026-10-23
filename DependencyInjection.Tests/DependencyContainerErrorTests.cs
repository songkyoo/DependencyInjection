// ReSharper disable AccessToModifiedClosure
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
        [Values] bool resolveFromChild
    )
    {
        var key = Key.Of<string>(tag);
        var returnNull = true;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(key, factory: (_, ref _) => returnNull ? null! : "Hello", lifeTime);
        });
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
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(key, factory: (_, ref _) => (string)null!, cleanup: _ => { });
        });
        var ex = Assert.Throws<InvalidOperationException>(() => container.Resolve(key));

        Assert.That(ex!.Message, Does.Contain("Factory returned null"));
        Assert.That(ex.Message, Does.Contain(key.ToString()));
    }

    [Test]
    public void CyclicDependency_ShouldThrow_WithResolutionPath()
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(Key.Of<A>(), factory: (resolver, ref _) => new A(b: resolver.Resolve<B>()), LifeTime.Transient);
            registrar.Register(Key.Of<B>(), factory: (resolver, ref _) => new B(c: resolver.Resolve<C>()), LifeTime.Transient);
            registrar.Register(Key.Of<C>(), factory: (resolver, ref _) => new C(a: resolver.Resolve<A>()), LifeTime.Transient);
        });
        var ex = Assert.Throws<InvalidOperationException>(() => container.Resolve(Key.Of<A>()));

        Assert.That(ex!.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+A"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+B"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+C"));
    }
}
