namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerErrorTests
{
    #pragma warning disable CS9113
    private sealed class A(B b);

    private sealed class B(C c);

    private sealed class C(A a);
    #pragma warning restore CS9113

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
        Assert.That(ex.Message, Does.Contain("Key { Type = Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+A"));
        Assert.That(ex.Message, Does.Contain("Key { Type = Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+B"));
        Assert.That(ex.Message, Does.Contain("Key { Type = Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+C"));
    }
}
