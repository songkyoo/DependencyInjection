namespace Macaron.DependencyInjection.Tests;

public class InjectionContextTests
{
    [Test]
    public void DefaultContext_ShouldRejectResolverAccess()
    {
        Assert.Throws<InvalidOperationException>(() => _ = default(InjectionContext<object>).Resolver);
    }

    [Test]
    public void For_ShouldPreserveSelectedResolver()
    {
        using var container = DependencyContainerFactory.Create(_ => { });
        using var scope = container.CreateTransientScope();
        using var child = container.CreateScope();

        Assert.That(container.For<object>().Resolver, Is.SameAs(container));
        Assert.That(child.For<object>().Resolver, Is.SameAs(child));
        Assert.That(scope.For<object>().Resolver, Is.SameAs(scope));
    }
}
