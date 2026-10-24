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
        [Values] bool useDefaultTag
    )
    {
        var tag = useDefaultTag ? null : "service";
        var failCreation = false;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<string>(factory: (_, ref _) => failCreation ? null! : "Hello", tag: tag);
        });

        failCreation = true;

        var exception = Assert.Throws<AggregateException>(() => root.CreateScope());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(1));
        Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain(typeof(string).ToString()));
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain(tag?.ToString() ?? "null"));
        Assert.That(root.Resolve<string>(tag), Is.EqualTo("Hello"));

        failCreation = false;

        using var child = root.CreateScope();

        Assert.That(child.Resolve<string>(tag), Is.EqualTo("Hello"));
    }

    [Test]
    public void CyclicDependency_ShouldThrow_WithResolutionPath()
    {
        var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<A>(factory: (resolver, ref _) => new A(b: resolver.Resolve<B>()), Lifetime.Transient, tag: null);
            registrar.Register<B>(factory: (resolver, ref _) => new B(c: resolver.Resolve<C>()), Lifetime.Transient, tag: null);
            registrar.Register<C>(factory: (resolver, ref _) => new C(a: resolver.Resolve<A>()), Lifetime.Transient, tag: null);
        });
        var ex = Assert.Throws<InvalidOperationException>(() => container.Resolve<A>(null));

        Assert.That(ex!.Message, Does.Contain("Cyclic dependency detected"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+A"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+B"));
        Assert.That(ex.Message, Does.Contain("Macaron.DependencyInjection.Tests.DependencyContainerErrorTests+C"));
    }
}
