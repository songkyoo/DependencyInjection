// ReSharper disable AccessToModifiedClosure
namespace Macaron.DependencyInjection.Tests;

public class TransientScopeLifecycleTests
{
    private sealed class Resource;

    private sealed record Service(Resource Resource);

    [Test]
    public void NestedFactoryAndFailedCollectorCleanup_ShouldRejectLifecycleChanges_WithoutInvalidatingScopes()
    {
        var fail = true;
        var failure = new ArgumentException("Dependency creation failed.");
        var cleanupCalls = 0;
        var installerCalls = 0;
        IDependencyContainer? container = null;
        TransientScope active = null!;
        TransientScope sibling = null!;

        container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(factory: (_, ref collector) =>
            {
                AssertChangesRejected();

                if (fail)
                {
                    collector.Add(new object(), _ =>
                    {
                        cleanupCalls++;
                        AssertChangesRejected();
                    });

                    throw failure;
                }

                return new Resource();
            });
            registrar.RegisterTransient<Service>(factory: (resolver, ref collector) =>
            {
                AssertChangesRejected();

                if (fail)
                {
                    collector.Add(new object(), _ =>
                    {
                        cleanupCalls++;

                        AssertChangesRejected();
                    });

                    var exception = Assert.Throws<ArgumentException>(() => resolver.Resolve<Resource>());

                    Assert.That(exception, Is.SameAs(failure));
                    AssertChangesRejected();
                }

                var resource = resolver.Resolve<Resource>();

                AssertChangesRejected();

                return new Service(resource);
            });
        });

        using (container)
        {
            active = container.CreateTransientScope();
            sibling = container.CreateTransientScope();

            var exception = Assert.Throws<ArgumentException>(() => active.Resolve<Service>(null));

            Assert.That(exception, Is.SameAs(failure));
            Assert.That(cleanupCalls, Is.EqualTo(3));
            Assert.That(installerCalls, Is.Zero);
            Assert.That(active.Contains<Service>(null), Is.True);
            Assert.That(sibling.Contains<Service>(null), Is.True);

            fail = false;

            Assert.That(active.Resolve<Service>(null).Resource, Is.Not.Null);

            active.Dispose();
            sibling.Dispose();

            using var replacement = container.CreateTransientScope();
            using var child = container.CreateScope(_ => installerCalls++);

            Assert.That(installerCalls, Is.EqualTo(1));
        }

        #region Local Functions
        void AssertChangesRejected()
        {
            Assert.That(container!.CreateTransientScope, Throws.InvalidOperationException);
            Assert.That(() => container.CreateScope(), Throws.InvalidOperationException);
            Assert.That(() => container.CreateScope(_ => installerCalls++), Throws.InvalidOperationException);
            Assert.That(container.Dispose, Throws.InvalidOperationException);
            Assert.That(active.Dispose, Throws.InvalidOperationException);
            Assert.That(sibling.Dispose, Throws.InvalidOperationException);
            Assert.That(active.Contains<Service>(null), Is.True);
        }
        #endregion
    }

    [Test]
    public void ScopeCleanup_ShouldRejectOtherLifecycleChanges_AndAllowRepeatedOwnDisposal([Values] bool cleanupFails)
    {
        TransientScope active = null!;
        TransientScope sibling = null!;
        TransientScope ended = null!;
        IDependencyContainer? container = null;
        var cleanupCalls = 0;
        var installerCalls = 0;
        var failure = new ArgumentException("Cleanup failed.");

        container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterSingleton<Resource>(factory: () => new Resource());
            registrar.RegisterTransient<object>(factory: () => new object(), cleanup: _ =>
            {
                cleanupCalls++;

                Assert.That(active.Dispose, Throws.Nothing);
                Assert.That(ended.Dispose, Throws.Nothing);
                Assert.That(() => active.Contains<object>(null), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(container!.CreateTransientScope, Throws.InvalidOperationException);
                Assert.That(() => container.CreateScope(), Throws.InvalidOperationException);
                Assert.That(() => container.CreateScope(_ => installerCalls++), Throws.InvalidOperationException);
                Assert.That(container.Dispose, Throws.InvalidOperationException);
                Assert.That(sibling.Dispose, Throws.InvalidOperationException);
                Assert.That(sibling.Resolve<Resource>(null), Is.SameAs(container.Resolve<Resource>()));

                if (cleanupFails)
                {
                    throw failure;
                }
            });
        });

        using (container)
        {
            active = container.CreateTransientScope();
            sibling = container.CreateTransientScope();
            ended = container.CreateTransientScope();
            ended.Dispose();
            active.Resolve<object>(null);

            if (cleanupFails)
            {
                var exception = Assert.Throws<AggregateException>(active.Dispose);
                Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { failure }));
            }
            else
            {
                Assert.That(active.Dispose, Throws.Nothing);
            }

            Assert.That(cleanupCalls, Is.EqualTo(1));
            Assert.That(installerCalls, Is.Zero);
            Assert.That(sibling.Contains<object>(null), Is.True);
            Assert.That(active.Dispose, Throws.Nothing);

            sibling.Dispose();

            using var replacement = container.CreateTransientScope();
            using var child = container.CreateScope(_ => installerCalls++);

            Assert.That(installerCalls, Is.EqualTo(1));
        }
    }
}
