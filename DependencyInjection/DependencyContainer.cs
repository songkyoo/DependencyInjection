using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

internal sealed class DependencyContainer(
    DependencyContainer? parent,
    ImmutableDictionary<RegistrationKey, TypeRegistration> registry
) : IDependencyContainer
{
    #region Types
    private struct TransientScopeSlot
    {
        internal TransientScope? Scope;
        internal List<CleanupEntry>? Cleanups;

        internal void Deconstruct(out TransientScope? scope, out List<CleanupEntry>? cleanups)
        {
            scope = Scope;
            cleanups = Cleanups;
        }
    }
    #endregion

    #region Static
    private static readonly ImmutableDictionary<RegistrationKey, TypeRegistration> EmptyRegistry =
        ImmutableDictionary.Create<RegistrationKey, TypeRegistration>();

    internal static DependencyContainer Create(
        DependencyContainer? parent,
        ImmutableDictionary<RegistrationKey, TypeRegistration> registry
    )
    {
        var container = new DependencyContainer(parent, registry);

        try
        {
            container.InitializeSharedInstances();

            return container;
        }
        catch (Exception creationError)
        {
            try
            {
                container.Dispose();
            }
            catch (Exception disposalError)
            {
                throw new AggregateException(creationError, disposalError);
            }

            throw new AggregateException(creationError);
        }
    }

    private static void CleanupEntries(List<CleanupEntry> cleanups, ref List<Exception>? exceptions)
    {
        for (var i = cleanups.Count - 1; i >= 0; i--)
        {
            try
            {
                cleanups[i].Invoke();
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
    private readonly Dictionary<RegistrationKey, object> _instances = new();
    private readonly List<CleanupEntry> _cleanups = [];
    private readonly Stack<RegistrationKey> _resolutionStack = new();
    private int _creationDepth;
    private bool _isDisposed;
    private bool _isCleaningTransientScope;

    private TransientScopeSlot[] _transientScopeSlots = [];
    private int _transientScopeSlotCount;
    private int _freeTransientScopeSlotCount;

    private DependencyContainer? _parent = parent;
    private ImmutableDictionary<RegistrationKey, TypeRegistration> _registry = registry;
    #endregion

    #region IDependencyContainer Interface
    public T Resolve<T>(object? tag = null)
    {
        return (T)ResolveRecursively(
            new RegistrationKey(typeof(T), tag),
            scopedInstances: _instances,
            dependencyContainer: this,
            resolver: this
        );
    }

    public bool Contains<T>(object? tag = null)
    {
        ThrowIfDisposed();

        var key = new RegistrationKey(typeof(T), tag);

        return key.IsResolver
            || _registry.ContainsKey(key)
            || (_parent as IDependencyResolver)?.Contains<T>(tag) is true;
    }

    public IDependencyContainer CreateScope()
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateScope));

        return Create(parent: this, registry: EmptyRegistry);
    }

    public IDependencyContainer CreateScope(IDependencyInstaller installer)
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateScope));

        var builder = new DependencyContainerBuilder();

        installer.Install(builder);

        return builder.Build(parent: this);
    }

    public TransientScope CreateTransientScope()
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateTransientScope));

        var index = AllocateTransientScopeSlot();
        var transientScope = new TransientScope(this, index);

        _transientScopeSlots[index].Scope = transientScope;

        return transientScope;
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

        for (var i = _transientScopeSlotCount - 1; i >= 0; i--)
        {
            if (_transientScopeSlots[i].Scope is null)
            {
                continue;
            }

            try
            {
                DisposeTransientScopeSlot(i);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        _transientScopeSlots = [];
        _transientScopeSlotCount = 0;
        _freeTransientScopeSlotCount = 0;

        CleanupEntries(_cleanups, ref exceptions);
        _instances.Clear();

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }
    #endregion

    #region Methods
    private void InitializeSharedInstances()
    {
        ThrowIfDisposed();

        var visitedKeys = new HashSet<RegistrationKey>();

        for (
            var registrationContainer = this;
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

    internal T Resolve<T>(object? tag, TransientScope transientScope)
    {
        ThrowIfTransientScopeInvalid(transientScope);

        return (T)ResolveRecursively(new RegistrationKey(typeof(T), tag), _instances, dependencyContainer: this, transientScope);
    }

    internal bool Contains<T>(object? tag, TransientScope transientScope)
    {
        ThrowIfTransientScopeInvalid(transientScope);

        return Contains<T>(tag);
    }

    internal void DisposeTransientScope(TransientScope transientScope)
    {
        ThrowIfLifecycleChangeBlocked(nameof(Dispose));
        ThrowIfTransientScopeInvalid(transientScope);

        DisposeTransientScopeSlot(transientScope.Index);
    }

    private object ResolveRecursively(
        RegistrationKey key,
        Dictionary<RegistrationKey, object> scopedInstances,
        DependencyContainer dependencyContainer,
        IDependencyResolver resolver
    )
    {
        ThrowIfDisposed();

        if (key.IsResolver)
        {
            return resolver;
        }

        foreach (var resolvingKey in _resolutionStack)
        {
            if (resolvingKey == key)
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
                throw new InvalidOperationException($"Service not found: {key}");
            }

            return _parent.ResolveRecursively(key, scopedInstances, dependencyContainer, resolver);
        }
        finally
        {
            _resolutionStack.Pop();
        }
    }

    private object GetOrAdd(
        Dictionary<RegistrationKey, object> instances,
        RegistrationKey key,
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
        RegistrationKey key,
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

            for (var i = entries.Length - 1; i >= 0; i--)
            {
                try
                {
                    entries[i].Invoke();
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

    private int AllocateTransientScopeSlot()
    {
        if (_freeTransientScopeSlotCount > 0)
        {
            for (var i = 0; i < _transientScopeSlotCount; i++)
            {
                if (_transientScopeSlots[i].Scope is null)
                {
                    _freeTransientScopeSlotCount -= 1;

                    return i;
                }
            }
        }

        if (_transientScopeSlotCount == _transientScopeSlots.Length)
        {
            var capacity = _transientScopeSlots.Length == 0 ? 4 : _transientScopeSlots.Length * 2;

            Array.Resize(ref _transientScopeSlots, capacity);
        }

        var newIndex = _transientScopeSlotCount;

        _transientScopeSlotCount += 1;

        return newIndex;
    }

    private List<CleanupEntry> GetTransientCleanups(IDependencyResolver resolver)
    {
        if (resolver is not TransientScope transientScope)
        {
            return _cleanups;
        }

        ref var slot = ref GetTransientScopeSlot(transientScope);

        return slot.Cleanups ??= [];
    }

    private ref TransientScopeSlot GetTransientScopeSlot(TransientScope transientScope)
    {
        ThrowIfTransientScopeInvalid(transientScope);

        return ref _transientScopeSlots[transientScope.Index];
    }

    private void DisposeTransientScopeSlot(int index)
    {
        var (scope, cleanups) = _transientScopeSlots[index];

        _transientScopeSlots[index] = default;
        _freeTransientScopeSlotCount += 1;

        scope!.Invalidate();

        _isCleaningTransientScope = true;

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
            _isCleaningTransientScope = false;
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

        if (_isCleaningTransientScope)
        {
            throw new InvalidOperationException($"Cannot call {operation} while a TransientScope is being disposed.");
        }
    }

    private void ThrowIfTransientScopeInvalid(TransientScope transientScope)
    {
        ThrowIfDisposed();

        var index = transientScope.Index;

        if ((uint)index >= (uint)_transientScopeSlotCount
            || !ReferenceEquals(_transientScopeSlots[index].Scope, transientScope)
           )
        {
            throw new ObjectDisposedException(nameof(TransientScope));
        }
    }
    #endregion
}

