namespace Macaron.DependencyInjection.Tests;

public class InjectionContextTests
{
    [Test]
    public void DefaultContext_ShouldRejectResolverAccess()
    {
        Assert.Throws<InvalidOperationException>(() => _ = default(InjectionContext<object>).Resolver);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void For_ShouldPreserveSelectedResolver(bool transientScope)
    {
        using var container = DependencyContainerFactory.Create(_ => { });
        using var scope = container.CreateTransientScope();
        IDependencyResolver resolver = transientScope ? scope : container;

        Assert.That(resolver.For<object>().Resolver, Is.SameAs(resolver));
    }
}
