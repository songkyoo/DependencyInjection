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
    public void InheritedScopedNullFactoryResult_ShouldFailChildCreation_AndPreserveParent(
        [Values] bool useDefaultKey
    )
    {
        var key = useDefaultKey ? Key<string>.Default : new Key<string>("service");
        var failCreation = false;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped(
                key,
                factory: (_, ref _) => failCreation ? null! : "Hello"
            );
        });

        failCreation = true;

        var exception = Assert.Throws<AggregateException>(() => root.CreateScope());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(1));
        Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain(typeof(string).ToString()));
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain(key.ToString()));
        Assert.That(root.Resolve(key), Is.EqualTo("Hello"));

        failCreation = false;

        using var child = root.CreateScope();

        Assert.That(child.Resolve(key), Is.EqualTo("Hello"));
    }

    [Test]
    public void CyclicDependency_ShouldThrow_WithResolutionPath()
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(Key<A>.Default, factory: (resolver, ref _) => new A(b: resolver.Resolve<B>()), Lifetime.Transient);
            registrar.Register(Key<B>.Default, factory: (resolver, ref _) => new B(c: resolver.Resolve<C>()), Lifetime.Transient);
            registrar.Register(Key<C>.Default, factory: (resolver, ref _) => new C(a: resolver.Resolve<A>()), Lifetime.Transient);
        });
        var ex = Assert.Throws<InvalidOperationException>(() => container.Resolve(Key<A>.Default));

        Assert.That(ex!.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+A"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+B"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+C"));
    }
}
