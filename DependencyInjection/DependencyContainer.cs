using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

internal sealed class DependencyContainer(
    DependencyContainer? parent,
    ImmutableDictionary<object, TypeRegistration> registry
) : IDependencyContainer
{
    #region Types
    private struct LifetimeScopeSlot
    {
        public LifetimeScope? Scope;
        public List<CleanupEntry>? Cleanups;

        public void Deconstruct(out LifetimeScope? scope, out List<CleanupEntry>? cleanups)
        {
            scope = Scope;
            cleanups = Cleanups;
        }
    }
    #endregion

    #region Static
    private static void CleanupEntries(List<CleanupEntry> cleanups, ref List<Exception>? exceptions)
    {
        for (var index = cleanups.Count - 1; index >= 0; index--)
        {
            try
            {
                cleanups[index].Invoke();
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        cleanups.Clear();
    }
    #endregion

    #region Fields
    private readonly Dictionary<object, object> _instances = new(KeyReferenceComparer.Instance);
    private readonly List<CleanupEntry> _cleanups = [];
    private readonly Stack<object> _resolutionStack = new();
    private int _creationDepth;
    private bool _isDisposed;
    private bool _isCleaningLifetimeScope;

    private LifetimeScopeSlot[] _lifetimeScopeSlots = [];
    private int _lifetimeScopeSlotCount;
    private int _freeLifetimeScopeSlotCount;

    private DependencyContainer? _parent = parent;
    private ImmutableDictionary<object, TypeRegistration> _registry = registry;
    #endregion

    #region IDependencyContainer Interface
    public T Resolve<T>(Key<T> key)
    {
        return (T)ResolveRecursively(
            key,
            scopedInstances: _instances,
            dependencyContainer: this,
            resolver: this
        );
    }

    public bool Contains<T>(Key<T> key)
    {
        ThrowIfDisposed();

        return _registry.ContainsKey(key) || (_parent as IDependencyResolver)?.Contains(key) is true;
    }

    public IDependencyContainer CreateScope(IDependencyInstaller installer)
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateScope));

        var builder = new DependencyContainerBuilder();

        installer.Install(builder);

        return builder.Build(parent: this);
    }

    public LifetimeScope CreateLifetimeScope()
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateLifetimeScope));

        var index = AllocateLifetimeScopeSlot();
        var lifetimeScope = new LifetimeScope(this, index);

        _lifetimeScopeSlots[index].Scope = lifetimeScope;

        return lifetimeScope;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        ThrowIfLifecycleChangeBlocked(nameof(Dispose));

        _isDisposed = true;
        _parent = null;
        _registry = _registry.Clear();

        List<Exception>? exceptions = null;

        for (var index = _lifetimeScopeSlotCount - 1; index >= 0; index--)
        {
            if (_lifetimeScopeSlots[index].Scope is null)
            {
                continue;
            }

            try
            {
                DisposeLifetimeScopeSlot(index);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        _lifetimeScopeSlots = [];
        _lifetimeScopeSlotCount = 0;
        _freeLifetimeScopeSlotCount = 0;

        CleanupEntries(_cleanups, ref exceptions);
        _instances.Clear();

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }
    #endregion

    #region Methods
    public void InitializeSharedInstances()
    {
        ThrowIfDisposed();

        var visitedKeys = new HashSet<object>(KeyReferenceComparer.Instance);

        for (
            DependencyContainer? registrationContainer = this;
            registrationContainer is not null;
            registrationContainer = registrationContainer._parent
        )
        {
            registrationContainer.ThrowIfDisposed();

            foreach (var (key, registration) in registrationContainer._registry)
            {
                if (!visitedKeys.Add(key))
                {
                    continue;
                }

                var isInitializationTarget = registration is TypeRegistration.Scoped
                    || (ReferenceEquals(registrationContainer, this) && registration is TypeRegistration.Singleton);

                if (!isInitializationTarget)
                {
                    continue;
                }

                ResolveRecursively(
                    key,
                    scopedInstances: _instances,
                    dependencyContainer: this,
                    resolver: this
                );
            }
        }
    }

    internal T Resolve<T>(Key<T> key, LifetimeScope lifetimeScope)
    {
        ThrowIfLifetimeScopeInvalid(lifetimeScope);

        return (T)ResolveRecursively(key, _instances, dependencyContainer: this, lifetimeScope);
    }

    internal bool Contains<T>(Key<T> key, LifetimeScope lifetimeScope)
    {
        ThrowIfLifetimeScopeInvalid(lifetimeScope);

        return Contains(key);
    }

    internal void DisposeLifetimeScope(LifetimeScope lifetimeScope)
    {
        ThrowIfLifecycleChangeBlocked(nameof(Dispose));
        ThrowIfLifetimeScopeInvalid(lifetimeScope);

        DisposeLifetimeScopeSlot(lifetimeScope.Index);
    }

    private object ResolveRecursively(
        object key,
        Dictionary<object, object> scopedInstances,
        DependencyContainer dependencyContainer,
        IDependencyResolver resolver
    )
    {
        ThrowIfDisposed();

        foreach (var resolvingKey in _resolutionStack)
        {
            if (ReferenceEquals(resolvingKey, key))
            {
                throw new InvalidOperationException($"Cyclic dependency detected: {string.Join(" -> ", _resolutionStack.Reverse().Append(key))}");
            }
        }

        _resolutionStack.Push(key);

        try
        {
            if (_registry.TryGetValue(key, out var typeRegistration))
            {
                return typeRegistration switch
                {
                    TypeRegistration.Transient { Factory: var factory, Cleanup: var cleanup } =>
                        dependencyContainer.CreateInstance(
                            key,
                            factory,
                            cleanup,
                            resolver,
                            dependencyContainer.GetTransientCleanups(resolver)
                        ),
                    TypeRegistration.Scoped { Factory: var factory, Cleanup: var cleanup } =>
                        dependencyContainer.GetOrAdd(
                            scopedInstances,
                            key,
                            factory,
                            cleanup
                        ),
                    TypeRegistration.Singleton { Factory: var factory, Cleanup: var cleanup } =>
                        GetOrAdd(
                            _instances,
                            key,
                            factory,
                            cleanup
                        ),
                    _ => throw new InvalidOperationException($"Not supported type registration: {typeRegistration}")
                };
            }

            if (_parent == null)
            {
                throw new InvalidOperationException($"Key not found: {key}");
            }

            return _parent.ResolveRecursively(key, scopedInstances, dependencyContainer, resolver);
        }
        finally
        {
            _resolutionStack.Pop();
        }
    }

    private object GetOrAdd(
        Dictionary<object, object> instances,
        object key,
        DependencyFactory<object> factory,
        Action<object>? cleanup
    )
    {
        if (instances.TryGetValue(key, out var instance))
        {
            return instance;
        }

        var newInstance = CreateInstance(key, factory, cleanup, resolver: this, cleanups: _cleanups);

        instances.Add(key, newInstance);

        return newInstance;
    }

    private object CreateInstance(
        object key,
        DependencyFactory<object> factory,
        Action<object>? cleanup,
        IDependencyResolver resolver,
        List<CleanupEntry> cleanups
    )
    {
        var cleanupCollector = new CleanupCollector();
        object instance;

        _creationDepth += 1;

        try
        {
            instance = factory.Invoke(resolver, key, ref cleanupCollector);

            ThrowIfDisposed();
        }
        catch (Exception exception)
        {
            CleanupFailedFactory(in cleanupCollector, exception);

            throw;
        }
        finally
        {
            _creationDepth -= 1;
        }

        cleanups.Add(new CleanupEntry(instance, cleanup));

        foreach (var entry in cleanupCollector.Entries)
        {
            cleanups.Add(entry);
        }

        return instance;

        #region Local Functions
        static void CleanupFailedFactory(in CleanupCollector cleanupCollector, Exception factoryException)
        {
            var exceptions = new List<Exception> { factoryException };
            var entries = cleanupCollector.Entries;

            for (var index = entries.Length - 1; index >= 0; index--)
            {
                try
                {
                    entries[index].Invoke();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }
            }

            if (exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }
        }
        #endregion
    }

    private int AllocateLifetimeScopeSlot()
    {
        if (_freeLifetimeScopeSlotCount > 0)
        {
            for (var i = 0; i < _lifetimeScopeSlotCount; i++)
            {
                if (_lifetimeScopeSlots[i].Scope is null)
                {
                    _freeLifetimeScopeSlotCount -= 1;

                    return i;
                }
            }
        }

        if (_lifetimeScopeSlotCount == _lifetimeScopeSlots.Length)
        {
            var capacity = _lifetimeScopeSlots.Length == 0 ? 4 : _lifetimeScopeSlots.Length * 2;

            Array.Resize(ref _lifetimeScopeSlots, capacity);
        }

        var newIndex = _lifetimeScopeSlotCount;

        _lifetimeScopeSlotCount += 1;

        return newIndex;
    }

    private List<CleanupEntry> GetTransientCleanups(IDependencyResolver resolver)
    {
        if (resolver is not LifetimeScope lifetimeScope)
        {
            return _cleanups;
        }

        ref var slot = ref GetLifetimeScopeSlot(lifetimeScope);

        return slot.Cleanups ??= [];
    }

    private ref LifetimeScopeSlot GetLifetimeScopeSlot(LifetimeScope lifetimeScope)
    {
        ThrowIfLifetimeScopeInvalid(lifetimeScope);

        return ref _lifetimeScopeSlots[lifetimeScope.Index];
    }

    private void DisposeLifetimeScopeSlot(int index)
    {
        var (scope, cleanups) = _lifetimeScopeSlots[index];

        _lifetimeScopeSlots[index] = default;
        _freeLifetimeScopeSlotCount += 1;

        scope!.Invalidate();

        _isCleaningLifetimeScope = true;

        List<Exception>? exceptions = null;

        try
        {
            if (cleanups is not null)
            {
                CleanupEntries(cleanups, ref exceptions);
            }
        }
        finally
        {
            _isCleaningLifetimeScope = false;
        }

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(objectName: nameof(DependencyContainer));
        }
    }

    private void ThrowIfLifecycleChangeBlocked(string operation)
    {
        if (_creationDepth > 0)
        {
            throw new InvalidOperationException($"Cannot call {operation} while a service is being created.");
        }

        if (_isCleaningLifetimeScope)
        {
            throw new InvalidOperationException($"Cannot call {operation} while a LifetimeScope is being disposed.");
        }
    }

    private void ThrowIfLifetimeScopeInvalid(LifetimeScope lifetimeScope)
    {
        ThrowIfDisposed();

        var index = lifetimeScope.Index;

        if ((uint)index >= (uint)_lifetimeScopeSlotCount
            || !ReferenceEquals(_lifetimeScopeSlots[index].Scope, lifetimeScope)
           )
        {
            throw new ObjectDisposedException(nameof(LifetimeScope));
        }
    }
    #endregion
}

