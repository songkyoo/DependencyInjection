// ReSharper disable AccessToDisposedClosure
// ReSharper disable DisposeOnUsingVariable
namespace Macaron.DependencyInjection.Tests;

[TestFixture("root")]
[TestFixture("child")]
[TestFixture("transient")]
[TestFixture("childTransient")]
public class NullableServiceResolutionTests(string resolverKind)
{
    [TestCase(null)]
    [TestCase("service")]
    public void NullableServiceType_ShouldRemainMissingWhenUnderlyingTypeIsRegistered(string? tag)
    {
        using var root = DependencyContainerFactory.Create(registrar => registrar.RegisterSingleton(instance: 42, tag));
        using var child = root.CreateScope();
        using var transient = root.CreateTransientScope();
        using var childTransient = child.CreateTransientScope();
        var resolver = SelectResolver(root, child, transient, childTransient);
        var fallbackCalls = 0;

        var resolveError = Assert.Throws<InvalidOperationException>(() => resolver.Resolve<int?>(tag));

        Assert.That(resolveError!.Message, Does.Contain(typeof(int?).ToString()));
        Assert.That(resolver.Contains<int?>(tag), Is.False);
        Assert.That(resolver.ResolveOrElse<int?>(defaultValue: 0, tag), Is.EqualTo(0));
        Assert.That(
            resolver.ResolveOrElse<int?>(
                factory: () =>
                {
                    fallbackCalls++;

                    return 0;
                },
                tag
            ),
            Is.EqualTo(0)
        );
        Assert.That(
            resolver.ResolveOrElse<int?>(
                factory: currentResolver =>
                {
                    Assert.That(currentResolver, Is.SameAs(resolver));

                    fallbackCalls++;

                    return 0;
                },
                tag
            ),
            Is.EqualTo(0)
        );
        Assert.That(fallbackCalls, Is.EqualTo(2));
        Assert.That(resolver.Resolve<int>(tag), Is.EqualTo(42));
        Assert.That(resolver.ResolveOrNull<int>(tag), Is.EqualTo(42));
        Assert.That(resolver.ResolveOrNull<int>("missing"), Is.Null);
    }

    [Test]
    public void MissingNullableServiceType_ShouldUseNormalMissingServiceBehavior()
    {
        using var root = DependencyContainerFactory.Create(_ => { });
        using var child = root.CreateScope();
        using var transient = root.CreateTransientScope();
        using var childTransient = child.CreateTransientScope();
        var resolver = SelectResolver(root, child, transient, childTransient);

        Assert.That(() => resolver.Resolve<int?>(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(resolver.Contains<int?>(), Is.False);
        Assert.That(() => resolver.Resolve<DateTime?>(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(resolver.Contains<DateTime?>(), Is.False);
        Assert.That(resolver.ResolveOrElse<int?>(defaultValue: null), Is.Null);
        Assert.That(resolver.ResolveOrElse<int?>(factory: () => null), Is.Null);
        Assert.That(resolver.ResolveOrElse<int?>(factory: _ => null), Is.Null);
        Assert.That(resolver.Contains<int>(), Is.False);
        Assert.That(resolver.ResolveOrNull<int>(), Is.Null);
    }

    [Test]
    public void DisposedResolver_ShouldRejectNullableQueriesBeforeCallingFallback()
    {
        using var root = DependencyContainerFactory.Create(_ => { });
        using var child = root.CreateScope();
        using var transient = root.CreateTransientScope();
        using var childTransient = child.CreateTransientScope();
        var resolver = SelectResolver(root, child, transient, childTransient);
        var fallbackCalls = 0;

        ((IDisposable)resolver).Dispose();

        Assert.That(() => resolver.Resolve<int?>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => resolver.Contains<int?>(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => resolver.ResolveOrElse<int?>(defaultValue: null), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => resolver.ResolveOrElse<int?>(
            factory: () =>
            {
                fallbackCalls++;

                return 0;
            }),
            Throws.TypeOf<ObjectDisposedException>()
        );
        Assert.That(
            () => resolver.ResolveOrElse<int?>(factory: _ =>
            {
                fallbackCalls++;

                return 0;
            }),
            Throws.TypeOf<ObjectDisposedException>()
        );
        Assert.That(fallbackCalls, Is.Zero);
    }

    [Test]
    public void NestedNullableAndReferenceAnnotations_ShouldKeepServiceIdentityAndTagValues()
    {
        var values = new List<int?> { null, 42 };
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton(instance: values);
            registrar.RegisterSingleton(instance: "value", tag: 42);
        });
        using var child = root.CreateScope();
        using var transient = root.CreateTransientScope();
        using var childTransient = child.CreateTransientScope();
        var resolver = SelectResolver(root, child, transient, childTransient);
        int? tag = 42;

        Assert.That(resolver.Resolve<List<int?>>(), Is.SameAs(values));
        Assert.That(resolver.Contains<List<int?>>(), Is.True);
        Assert.That(resolver.Resolve<string?>(tag), Is.EqualTo("value"));
        Assert.That(resolver.Contains<string?>(tag), Is.True);
        Assert.That(resolver.ResolveOrElse<string?>(defaultValue: null), Is.Null);
    }

    private IDependencyResolver SelectResolver(
        IDependencyContainer root,
        IDependencyContainer child,
        TransientScope transient,
        TransientScope childTransient
    )
    {
        return resolverKind switch
        {
            "root" => root,
            "child" => child,
            "transient" => transient,
            "childTransient" => childTransient,
            _ => throw new ArgumentOutOfRangeException(nameof(resolverKind))
        };
    }
}
