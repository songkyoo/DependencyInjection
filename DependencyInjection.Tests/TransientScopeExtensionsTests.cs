// ReSharper disable DisposeOnUsingVariable
namespace Macaron.DependencyInjection.Tests;

public class TransientScopeExtensionsTests
{
    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed record Service(Resource Resource);

    [Test]
    public void ExistingExtensions_ShouldResolveRegisteredService_WithoutCallingFallback()
    {
        var namedKey = new Key<Resource>("named");
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterScoped<Resource>();
            registrar.RegisterScoped(namedKey);
        });
        using var scope = container.CreateTransientScope();
        var expected = container.Resolve<Resource>();
        var expectedNamed = container.Resolve(namedKey);

        Assert.That(scope.Contains<Resource>(), Is.True);
        Assert.That(scope.Resolve<Resource>(), Is.SameAs(expected));
        Assert.That(scope.ResolveOrNull<Resource>(), Is.SameAs(expected));
        Assert.That(scope.ResolveOrElse<Resource>(_ => throw new InvalidOperationException()), Is.SameAs(expected));
        Assert.That(scope.ResolveOrElse<Resource>(() => throw new InvalidOperationException()), Is.SameAs(expected));
        Assert.That(scope.ResolveOrElse(defaultValue: new Resource()), Is.SameAs(expected));
        Assert.That(scope.ResolveOrNull(namedKey), Is.SameAs(expectedNamed));
        Assert.That(scope.ResolveOrElse(namedKey, _ => throw new InvalidOperationException()), Is.SameAs(expectedNamed));
        Assert.That(scope.ResolveOrElse(namedKey, () => throw new InvalidOperationException()), Is.SameAs(expectedNamed));
        Assert.That(scope.ResolveOrElse(namedKey, defaultValue: new Resource()), Is.SameAs(expectedNamed));
    }

    [Test]
    public void FallbackResolver_ShouldBeSameScope_AndOwnNestedTransient([Values] bool useNamedKey)
    {
        using var container = DependencyContainerFactory.Create(registrar => registrar.RegisterTransient<Resource>());
        var scope = container.CreateTransientScope();
        IDependencyResolver? capturedResolver = null;
        Func<IDependencyResolver, Service> factory = resolver =>
        {
            capturedResolver = resolver;

            return new Service(resolver.Resolve<Resource>());
        };

        var service = useNamedKey
            ? scope.ResolveOrElse(new Key<Service>("missing"), factory)
            : scope.ResolveOrElse(factory);

        Assert.That(capturedResolver, Is.SameAs(scope));
        Assert.That(scope.Contains<Service>(), Is.False);
        Assert.That(scope.ResolveOrNull<Service>(), Is.Null);
        Assert.That(service.Resource.DisposeCount, Is.Zero);

        scope.Dispose();

        Assert.That(service.Resource.DisposeCount, Is.EqualTo(1));
        Assert.That(capturedResolver!.Resolve<Resource>, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void ResolveOrNullValueTypes_ShouldUseExactKeyAndDefaultKey()
    {
        var namedKey = new Key<int>("answer");
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(instance: 7);
            registrar.RegisterSingleton(namedKey, instance: 42);
        });
        using var scope = container.CreateTransientScope();

        Assert.That(scope.ResolveOrNull<int>(), Is.EqualTo(7));
        Assert.That(scope.ResolveOrNull(namedKey), Is.EqualTo(42));
        Assert.That(scope.ResolveOrNull(new Key<int>("answer")), Is.Null);
        Assert.That(scope.ResolveOrNull<long>(), Is.Null);
    }

    [Test]
    public void DisposedScopeExtensions_ShouldRejectBeforeCallingFallback([Values] bool disposeContainerFirst)
    {
        using var container = DependencyContainerFactory.Create(_ => { });
        var scope = container.CreateTransientScope();
        var fallbackCalls = 0;

        if (disposeContainerFirst)
        {
            container.Dispose();
        }
        else
        {
            scope.Dispose();
        }

        Assert.That(scope.Contains<Resource>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(scope.Resolve<Resource>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(scope.ResolveOrNull<Resource>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(scope.ResolveOrNull<int>, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(
            () => scope.ResolveOrElse<Resource>(_ =>
            {
                fallbackCalls++;

                return new Resource();
            }),
            Throws.TypeOf<ObjectDisposedException>()
        );
        Assert.That(
            () => scope.ResolveOrElse(() =>
            {
                fallbackCalls++;

                return new Resource();
            }),
            Throws.TypeOf<ObjectDisposedException>()
        );
        Assert.That(() => scope.ResolveOrElse(new Resource()), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(fallbackCalls, Is.Zero);
    }
}
