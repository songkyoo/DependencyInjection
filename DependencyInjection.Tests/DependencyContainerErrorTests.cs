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
    public void NullFactoryResultDuringResolve_ShouldThrow_WithRegistrationKey_AndAllowRetry(
        [Values(null, "service-tag")] string? tag
    )
    {
        var key = Key.Of<string>(tag);
        var returnNull = true;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(key, factory: (_, ref _) => returnNull ? null! : "Hello");
        });
        using var child = root.CreateScope();

        var ex = Assert.Throws<InvalidOperationException>(() => child.Resolve(key));

        Assert.That(ex!.Message, Does.Contain("Factory returned null"));
        Assert.That(ex.Message, Does.Contain(key.ToString()));

        returnNull = false;

        Assert.That(child.Resolve(key), Is.EqualTo("Hello"));
    }

    [Test]
    public void InheritedScopedInvalidFactoryResult_ShouldFailChildCreation_AndPreserveParent(
        [Values(null, "service-tag")] string? tag,
        [Values] bool wrongType
    )
    {
        var key = Key.Of<string>(tag);
        var failCreation = false;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped(
                typeof(string),
                tag,
                factory: (_, ref _) => failCreation ? (wrongType ? new object() : null!) : "Hello"
            );
        });

        failCreation = true;

        var exception = Assert.Throws<AggregateException>(() => root.CreateScope());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(1));
        Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain(typeof(string).ToString()));

        if (!wrongType)
        {
            Assert.That(exception.InnerExceptions[0].Message, Does.Contain(key.ToString()));
        }

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
