// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToModifiedClosure
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class CleanupResolutionTests
{
    private sealed class Resource(Action? onDispose = null) : IDisposable
    {
        public void Dispose() => onDispose?.Invoke();
    }

    [Test]
    public void LocalScopeCleanup_ShouldRejectResolveBeforeCallingFactory(
        [Values] Lifetime lifetime,
        [Values] bool throughOtherScope
    )
    {
        var factoryCalls = 0;
        IDependencyResolver requestResolver = null!;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: () =>
                {
                    factoryCalls++;

                    return new Resource();
                }, lifetime);
            registrar.RegisterTransient(factory: static () => new object(), cleanup: _ =>
            {
                Assert.That(requestResolver.Contains<Resource>(), Is.True);
                Assert.That(() => requestResolver.Resolve<Resource>(), Throws.InvalidOperationException);
            });
        });
        using var other = container.CreateLocalScope();
        using var scope = container.CreateLocalScope();

        requestResolver = throughOtherScope ? other : container;

        var callsBeforeCleanup = factoryCalls;

        scope.Resolve<object>();
        scope.Dispose();

        Assert.That(factoryCalls, Is.EqualTo(callsBeforeCleanup));
        Assert.That(requestResolver.Resolve<Resource>(), Is.Not.Null);
        Assert.That(factoryCalls, Is.EqualTo(lifetime == Lifetime.Transient ? 1 : callsBeforeCleanup));
    }

    [Test]
    public void DefaultDisposal_ShouldRejectResolve_AndRestoreResolutionAfterCleanup()
    {
        IDependencyResolver requestResolver = null!;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(factory: () => new Resource(() =>
            {
                Assert.That(() => requestResolver.Resolve<IDependencyResolver>(), Throws.InvalidOperationException);
            }));
        });
        using var other = container.CreateLocalScope();

        requestResolver = other;

        using var scope = container.CreateLocalScope();

        scope.Resolve<Resource>();
        scope.Dispose();

        Assert.That(other.Resolve<IDependencyResolver>(), Is.SameAs(other));
    }

    [Test]
    public void FailedFactoryCleanup_ShouldRejectResolve_PreserveErrorsAndAllowRetry(
        [Values] Lifetime lifetime,
        [Values] bool throughLocalScope
    )
    {
        var fail = true;
        var factoryCalls = 0;
        var cleanupOrder = new List<string>();
        var factoryFailure = new ArgumentException("Factory failed.");
        IDependencyResolver requestResolver = null!;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(factory: () =>
                {
                    factoryCalls++;

                    return new Resource();
                }, lifetime);
            registrar.RegisterTransient<string>(factory: (resolver, ref collector) =>
            {
                if (!fail)
                {
                    return "success";
                }

                collector.Add(new object(), _ => cleanupOrder.Add("first"));
                collector.Add(new object(), _ =>
                {
                    cleanupOrder.Add("rejected");
                    requestResolver.Resolve<Resource>();
                });
                collector.Add(new object(), _ =>
                {
                    cleanupOrder.Add("last");
                    Assert.That(resolver.Contains<Resource>(), Is.True);
                    Assert.That(() => resolver.Resolve<IDependencyResolver>(), Throws.InvalidOperationException);
                });

                throw factoryFailure;
            });
        });
        using var scope = container.CreateLocalScope();

        requestResolver = throughLocalScope ? scope : container;

        var callsBeforeCleanup = factoryCalls;
        var exception = Assert.Throws<AggregateException>(() => requestResolver.Resolve<string>());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(exception.InnerExceptions[0], Is.SameAs(factoryFailure));
        Assert.That(exception.InnerExceptions[1], Is.TypeOf<InvalidOperationException>());
        Assert.That(cleanupOrder, Is.EqualTo(new[] { "last", "rejected", "first" }));
        Assert.That(factoryCalls, Is.EqualTo(callsBeforeCleanup));

        fail = false;

        Assert.That(requestResolver.Resolve<Resource>(), Is.Not.Null);
        Assert.That(requestResolver.Resolve<string>(), Is.EqualTo("success"));
    }

    [Test]
    public void LocalScopeCleanup_ShouldRejectSelfReferenceAndMissingService_AndKeepDisposedExceptionPriority()
    {
        IDependencyContainer container = null!;
        LocalScope scope = null!;
        LocalScope other = null!;
        using var owner = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(factory: static () => new object(), cleanup: _ =>
            {
                Assert.That(() => container.Resolve<IDependencyResolver>(), Throws.InvalidOperationException);
                Assert.That(() => other.Resolve<IDependencyResolver>(), Throws.InvalidOperationException);
                var error = Assert.Throws<InvalidOperationException>(() => other.Resolve<Resource>());
                Assert.That(error!.Message, Does.Contain("cleanup"));
                Assert.That(() => scope.Resolve<object>(), Throws.TypeOf<ObjectDisposedException>());
            });
        });

        container = owner;

        using var active = container.CreateLocalScope();

        other = active;
        scope = container.CreateLocalScope();

        scope.Resolve<object>();
        scope.Dispose();
    }

    [TestCaseSource(nameof(RegisteredRequests))]
    public void ResolveExtension_ShouldRejectRegisteredServiceDuringCleanup(Action<IDependencyResolver> request)
    {
        IDependencyResolver resolver = null!;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton("registered");
            registrar.RegisterSingleton(42);
            registrar.RegisterTransient(factory: static () => new object(), cleanup: _ =>
            {
                Assert.That(() => request(resolver), Throws.InvalidOperationException);
            });
        });

        resolver = container;

        using var scope = container.CreateLocalScope();

        scope.Resolve<object>();
        scope.Dispose();
    }

    [Test]
    public void MissingServiceFallbacks_ShouldRunDuringCleanup_AndRejectNestedResolve()
    {
        IDependencyResolver resolver = null!;
        var fallback = new Resource();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(factory: static () => new object(), cleanup: _ =>
            {
                Assert.That(resolver.ResolveOrNull<Resource>(), Is.Null);
                Assert.That(IDependencyResolverValueTypeExtensions.ResolveOrNull<int>(resolver), Is.Null);
                Assert.That(resolver.ResolveOrElse(fallback), Is.SameAs(fallback));
                Assert.That(resolver.ResolveOrElse(() => fallback), Is.SameAs(fallback));
                Assert.That(resolver.ResolveOrElse(_ => fallback), Is.SameAs(fallback));
                Assert.That(
                    () => resolver.ResolveOrElse<Resource>(nested =>
                    {
                        nested.Resolve<IDependencyResolver>();

                        return fallback;
                    }),
                    Throws.InvalidOperationException
                );
            });
        });
        resolver = container;

        using var scope = container.CreateLocalScope();

        scope.Resolve<object>();
        scope.Dispose();
    }

    [Test]
    public void Cleanup_ShouldAllowResolveOnAnIndependentContainer()
    {
        using var independent = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(42));
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(factory: static () => new object(), cleanup: _ =>
            {
                Assert.That(independent.Resolve<int>(), Is.EqualTo(42));
            });
        });
        using var scope = container.CreateLocalScope();

        scope.Resolve<object>();
        scope.Dispose();
    }

    private static IEnumerable<TestCaseData> RegisteredRequests()
    {
        yield return new TestCaseData((Action<IDependencyResolver>)(resolver => resolver.ResolveOrNull<string>()))
            .SetName("Cleanup_ResolveOrNull_ReferenceType");
        yield return new TestCaseData((Action<IDependencyResolver>)(resolver => resolver.ResolveOrNull<int>()))
            .SetName("Cleanup_ResolveOrNull_ValueType");
        yield return new TestCaseData((Action<IDependencyResolver>)(resolver => resolver.ResolveOrElse("fallback")))
            .SetName("Cleanup_ResolveOrElse_Default");
        yield return new TestCaseData((Action<IDependencyResolver>)(resolver =>
                resolver.ResolveOrElse<string>(() => throw new AssertionException("Fallback must not run."))))
            .SetName("Cleanup_ResolveOrElse_Factory");
        yield return new TestCaseData((Action<IDependencyResolver>)(resolver =>
                resolver.ResolveOrElse<string>(_ => throw new AssertionException("Fallback must not run."))))
            .SetName("Cleanup_ResolveOrElse_ResolverFactory");
    }
}
