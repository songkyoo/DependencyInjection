// ReSharper disable DisposeOnUsingVariable
// ReSharper disable ObjectCreationAsStatement
using System.Diagnostics.CodeAnalysis;

namespace Macaron.DependencyInjection.Tests;

[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments")]
public class RegistrationSourceTests
{
    private struct ValueResource(List<int> disposeCounts) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            disposeCounts.Add(DisposeCount);
        }
    }

    [TestCase(Lifetime.Transient)]
    [TestCase(Lifetime.Scoped)]
    [TestCase(Lifetime.Singleton)]
    public void FactorySource_ShouldUseTheSelectedLifetimeAndCustomCleanup(Lifetime lifetime)
    {
        var calls = 0;
        var cleanedInstances = new List<object>();
        var source = new RegistrationSource.Factory<object>(() =>
        {
            calls++;

            return new object();
        });
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(source, lifetime, tag: "service", cleanup: cleanedInstances.Add);
        });

        var expectedInitializationCalls = lifetime == Lifetime.Transient ? 0 : 1;

        Assert.That(calls, Is.EqualTo(expectedInitializationCalls));

        var first = container.Resolve<object>("service");
        var second = container.Resolve<object>("service");
        var expectedCalls = lifetime == Lifetime.Transient ? 2 : 1;

        Assert.That(calls, Is.EqualTo(expectedCalls));
        Assert.That(ReferenceEquals(first, second), Is.EqualTo(lifetime != Lifetime.Transient));
        Assert.That(cleanedInstances, Is.Empty);

        container.Dispose();

        var expectedCleanups = lifetime == Lifetime.Transient ? new[] { second, first } : new[] { first };

        Assert.That(cleanedInstances, Is.EqualTo(expectedCleanups));
    }

    [TestCase(Lifetime.Transient)]
    [TestCase(Lifetime.Scoped)]
    [TestCase(Lifetime.Singleton)]
    public void ContextFactorySource_ShouldReceiveOwningResolverAndRetainCollectedCleanup(Lifetime lifetime)
    {
        IDependencyResolver? factoryResolver = null;
        var cleanupOrder = new List<string>();
        var source = new RegistrationSource.ContextFactory<object>((resolver, ref collector) =>
        {
            factoryResolver = resolver;
            collector.Add(new object(), _ => cleanupOrder.Add("collected"));

            return new object();
        });
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(source, lifetime, cleanup: _ => cleanupOrder.Add("service"));
        });
        using var scope = container.CreateLocalScope();

        scope.Resolve<object>();

        IDependencyResolver expectedResolver = lifetime == Lifetime.Transient ? scope : container;

        Assert.That(factoryResolver, Is.SameAs(expectedResolver));
        Assert.That(cleanupOrder, Is.Empty);

        scope.Dispose();

        var expectedScopeCleanup = lifetime == Lifetime.Transient
            ? new[] { "collected", "service" }
            : Array.Empty<string>();

        Assert.That(cleanupOrder, Is.EqualTo(expectedScopeCleanup));

        container.Dispose();

        Assert.That(cleanupOrder, Is.EqualTo(new[] { "collected", "service" }));
    }

    [Test]
    public void ReusedValueInstanceSource_ShouldBoxIndependentlyForEachContainer()
    {
        var disposeCounts = new List<int>();
        var source = new RegistrationSource.Instance<ValueResource>(new ValueResource(disposeCounts));
        using var first = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(source));
        using var second = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(source));
        var firstCopy = first.Resolve<ValueResource>();
        var secondCopy = second.Resolve<ValueResource>();

        first.Dispose();
        second.Dispose();

        Assert.That(disposeCounts, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(source.Value.DisposeCount, Is.Zero);
        Assert.That(firstCopy.DisposeCount, Is.Zero);
        Assert.That(secondCopy.DisposeCount, Is.Zero);
    }

    [Test]
    public void TransientValueFactorySource_ShouldCreateAndCleanSeparateBoxedValues()
    {
        var calls = 0;
        var disposeCounts = new List<int>();
        var source = new RegistrationSource.Factory<ValueResource>(() =>
        {
            calls++;

            return new ValueResource(disposeCounts);
        });
        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient(source));
        using var scope = container.CreateLocalScope();

        var first = scope.Resolve<ValueResource>();
        var second = scope.Resolve<ValueResource>();

        scope.Dispose();

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(disposeCounts, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(first.DisposeCount, Is.Zero);
        Assert.That(second.DisposeCount, Is.Zero);
    }

    [Test]
    public void NullInstanceSource_ShouldRejectTheValueDuringConstruction()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new RegistrationSource.Instance<object>(null!));

        Assert.That(exception!.ParamName, Is.EqualTo("instance"));
    }

    [Test]
    public void NullTransientFactoryResult_ShouldFailResolution_AndAllowRetry()
    {
        var calls = 0;
        var expected = new object();
        var source = new RegistrationSource.Factory<object>(() =>
        {
            calls++;

            return calls == 1 ? null! : expected;
        });
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(source, tag: "service");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => container.Resolve<object>("service"));
        var instance = container.Resolve<object>("service");

        Assert.That(exception!.Message, Does.Contain(typeof(object).ToString()));
        Assert.That(exception.Message, Does.Contain("service"));
        Assert.That(instance, Is.SameAs(expected));
        Assert.That(calls, Is.EqualTo(2));
    }
}
