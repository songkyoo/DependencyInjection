// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

[TestFixture(false)]
[TestFixture(true)]
public class DependencyResolverExtensionsTests(bool useTransientScope)
{
    private sealed class Service;

    [TestCase(null)]
    [TestCase("service")]
    public void RegisteredService_ShouldBeReturnedWithoutCallingFallback(string? tag)
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Service>(factory: () => new Service());
            registrar.RegisterSingleton<Service>(factory: () => new Service(), tag: "service");
        });
        using var scope = container.CreateTransientScope();
        IDependencyResolver resolver = useTransientScope ? scope : container;
        var expected = container.Resolve<Service>(tag);

        Assert.That(resolver.Contains<Service>(tag), Is.True);
        Assert.That(resolver.Resolve<Service>(tag), Is.SameAs(expected));
        Assert.That(resolver.ResolveOrNull<Service>(tag), Is.SameAs(expected));
        Assert.That(resolver.ResolveOrElse<Service>(_ => throw new InvalidOperationException(), tag), Is.SameAs(expected));
        Assert.That(resolver.ResolveOrElse<Service>(() => throw new InvalidOperationException(), tag), Is.SameAs(expected));
        Assert.That(resolver.ResolveOrElse(new Service(), tag), Is.SameAs(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("missing")]
    public void MissingTag_ShouldReportServiceIdentityAndUseFallbackWithoutMatchingAnotherTag(string? missingTag)
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Service>(factory: () => new Service(), tag: "service");

            if (missingTag is not null)
            {
                registrar.RegisterSingleton<Service>(factory: () => new Service());
            }
        });
        using var scope = container.CreateTransientScope();
        IDependencyResolver resolver = useTransientScope ? scope : container;
        var fallback = new Service();
        var factoryCalls = 0;

        Assert.That(resolver.Contains<Service>(missingTag), Is.False);

        var error = Assert.Throws<InvalidOperationException>(() => resolver.Resolve<Service>(missingTag));

        Assert.That(error!.Message, Does.Contain(typeof(Service).ToString()));
        Assert.That(error.Message, Does.Contain(missingTag ?? "null"));
        Assert.That(resolver.ResolveOrNull<Service>(missingTag), Is.Null);
        Assert.That(resolver.ResolveOrElse(fallback, missingTag), Is.SameAs(fallback));
        Assert.That(
            resolver.ResolveOrElse(
                factory: () =>
                {
                    factoryCalls++;

                    return fallback;
                },
                missingTag
            ),
            Is.SameAs(fallback)
        );
        Assert.That(
            resolver.ResolveOrElse(
                factory: currentResolver =>
                {
                    Assert.That(currentResolver, Is.SameAs(resolver));

                    factoryCalls++;

                    return fallback;
                },
                missingTag
            ),
            Is.SameAs(fallback)
        );
        Assert.That(factoryCalls, Is.EqualTo(2));
        Assert.That(resolver.Contains<Service>(missingTag), Is.False);
    }

    [TestCase(null)]
    [TestCase("service")]
    public void NullFactoryResult_ShouldThrowWithoutReturningNullOrUsingFallback(string? tag)
    {
        var factoryCalls = 0;
        var fallbackCalls = 0;
        var fallback = new Service();
        using var container = DependencyContainerFactory.Create(registrar =>
            registrar.RegisterTransient<Service>(factory: () =>
            {
                factoryCalls++;
                return null!;
            }, tag));
        using var scope = container.CreateTransientScope();
        IDependencyResolver resolver = useTransientScope ? scope : container;

        var error = Assert.Throws<InvalidOperationException>(() => resolver.ResolveOrNull<Service>(tag));

        Assert.That(error!.Message, Does.Contain(typeof(Service).ToString()));
        Assert.That(error.Message, Does.Contain(tag ?? "null"));
        Assert.That(() => resolver.ResolveOrElse<Service>(current =>
        {
            fallbackCalls++;
            return fallback;
        }, tag), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => resolver.ResolveOrElse<Service>(() =>
        {
            fallbackCalls++;
            return fallback;
        }, tag), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => resolver.ResolveOrElse(fallback, tag), Throws.TypeOf<InvalidOperationException>());

        Assert.That(factoryCalls, Is.EqualTo(4));
        Assert.That(fallbackCalls, Is.Zero);
    }

    [Test]
    public void ResolveOrNullValueTypes_ShouldUseExactTagAndDefaultTag()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(instance: 7);
            registrar.RegisterSingleton(instance: 42, tag: "answer");
        });
        using var scope = container.CreateTransientScope();
        IDependencyResolver resolver = useTransientScope ? scope : container;

        Assert.That(resolver.ResolveOrNull<int>(), Is.EqualTo(7));
        Assert.That(resolver.ResolveOrNull<int>("answer"), Is.EqualTo(42));
        Assert.That(resolver.ResolveOrNull<int>("missing"), Is.Null);
        Assert.That(resolver.ResolveOrNull<long>(), Is.Null);
    }

    [Test]
    public void DisposedResolver_ShouldRejectExtensionsBeforeCallingFallback()
    {
        using var container = DependencyContainerFactory.Create(_ => { });
        using var scope = container.CreateTransientScope();
        var fallbackCalls = 0;

        if (useTransientScope)
        {
            scope.Dispose();
        }
        else
        {
            container.Dispose();
        }

        IDependencyResolver[] disposedResolvers = useTransientScope ? [scope] : [container, scope];

        foreach (var disposedResolver in disposedResolvers)
        {
            Assert.That(() => disposedResolver.Contains<Service>(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => disposedResolver.Resolve<Service>(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => disposedResolver.ResolveOrNull<Service>(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => disposedResolver.ResolveOrNull<int>(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => disposedResolver.ResolveOrElse<Service>(_ =>
                {
                    fallbackCalls++;

                    return new Service();
                }),
                Throws.TypeOf<ObjectDisposedException>()
            );
            Assert.That(
                () => disposedResolver.ResolveOrElse(() =>
                {
                    fallbackCalls++;

                    return new Service();
                }),
                Throws.TypeOf<ObjectDisposedException>()
            );
            Assert.That(() => disposedResolver.ResolveOrElse(new Service()), Throws.TypeOf<ObjectDisposedException>());
        }

        Assert.That(fallbackCalls, Is.Zero);
    }
}
