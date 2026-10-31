// ReSharper disable DisposeOnUsingVariable
// ReSharper disable AccessToDisposedClosure

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
// ReSharper disable AccessToModifiedClosure

namespace Macaron.DependencyInjection.Tests;

[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments")]
public class LocalScopeSharedCleanupTests
{
    private sealed class Resource(int id) : IDisposable
    {
        public int Id { get; } = id;
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    [Test]
    public void InterleavedScopes_ShouldKeepReverseCleanupOrder_WhenReleasedStorageIsReused()
    {
        var nextId = 0;
        var cleanups = new List<int>();
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(
                factory: () => new Resource(++nextId),
                cleanup: resource => cleanups.Add(resource.Id)
            );
        });
        using var first = container.CreateLocalScope();
        using var second = container.CreateLocalScope();
        using var third = container.CreateLocalScope();

        first.Resolve<Resource>();
        second.Resolve<Resource>();
        first.Resolve<Resource>();
        third.Resolve<Resource>();
        second.Dispose();

        using var replacement = container.CreateLocalScope();

        replacement.Resolve<Resource>();
        first.Dispose();

        Assert.That(cleanups, Is.EqualTo(new[] { 2, 3, 1 }));
        Assert.That(() => second.Resolve<Resource>(), Throws.TypeOf<ObjectDisposedException>());

        third.Dispose();
        replacement.Dispose();

        Assert.That(cleanups, Is.EqualTo(new[] { 2, 3, 1, 4, 5 }));
    }

    [Test]
    public void CleanupReentry_ShouldKeepRemainingEntries_WhenAnotherScopeReusesAndGrowsStorage()
    {
        var nextId = 0;
        var cleanups = new List<int>();
        var createdDuringCleanup = new List<Resource>();
        LocalScope other = null!;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient<Resource>(
                factory: () => new Resource(++nextId),
                cleanup: resource =>
                {
                    resource.Dispose();
                    cleanups.Add(resource.Id);

                    if (resource.Id == 2)
                    {
                        for (var index = 0; index < 32; index++)
                        {
                            createdDuringCleanup.Add(other.Resolve<Resource>());
                        }
                    }
                }
            );
        });
        using var first = container.CreateLocalScope();
        using var second = container.CreateLocalScope();

        other = second;

        first.Resolve<Resource>();
        first.Resolve<Resource>();
        first.Dispose();

        Assert.That(cleanups, Is.EqualTo(new[] { 2, 1 }));
        Assert.That(createdDuringCleanup, Has.Count.EqualTo(32));
        Assert.That(createdDuringCleanup.All(resource => resource.DisposeCount == 0), Is.True);

        second.Dispose();

        var expectedOrder = new[] { 2, 1 }.Concat(Enumerable.Range(3, 32).Reverse());

        Assert.That(cleanups, Is.EqualTo(expectedOrder));
        Assert.That(createdDuringCleanup.All(resource => resource.DisposeCount == 1), Is.True);
    }

    [Test]
    public void DisposedScope_ShouldReleaseEntries_WhileContainerRemainsAlive()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterTransient(static () => new Resource(1));
        });
        var reference = CreateAndReleaseResource(container);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(reference.IsAlive, Is.False);

        GC.KeepAlive(container);

        #region Local Functions
        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference CreateAndReleaseResource(IDependencyContainer container)
        {
            using var scope = container.CreateLocalScope();
            var resource = scope.Resolve<Resource>();

            return new WeakReference(resource);
        }
        #endregion
    }
}
