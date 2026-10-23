using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

internal sealed class DependencyContainer(
    DependencyContainer? parent,
    ImmutableDictionary<object, TypeRegistration> registry
) : IDependencyContainer
{
    #region Fields
    private readonly Dictionary<object, object> _instances = new(KeyReferenceComparer.Instance);
    private readonly List<CleanupEntry> _cleanups = [];
    private readonly Stack<object> _resolutionStack = new();
    private int _creationDepth;
    private bool _isDisposed;

    private DependencyContainer? _parent = parent;
    private ImmutableDictionary<object, TypeRegistration> _registry = registry;
    #endregion

    #region IDependencyContainer Interface
    T IDependencyResolver.Resolve<T>(Key<T> key)
    {
        return (T)ResolveRecursively(key, scopedInstances: _instances, dependencyContainer: this);
    }

    bool IDependencyResolver.Contains<T>(Key<T> key)
    {
        ThrowIfDisposed();

        return _registry.ContainsKey(key) || (_parent as IDependencyResolver)?.Contains(key) is true;
    }

    public IDependencyContainer CreateScope(IDependencyInstaller installer)
    {
        ThrowIfDisposed();

        if (_creationDepth > 0)
        {
            throw new InvalidOperationException("Cannot call CreateScope while a service is being created.");
        }

        var builder = new DependencyContainerBuilder();

        installer.Install(builder);

        return builder.Build(parent: this);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_creationDepth > 0)
        {
            throw new InvalidOperationException("Cannot call Dispose while a service is being created.");
        }

        _isDisposed = true;
        _parent = null;
        _registry = _registry.Clear();

        List<Exception>? exceptions = null;

        for (var index = _cleanups.Count - 1; index >= 0; index--)
        {
            try
            {
                _cleanups[index].Invoke();
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        _cleanups.Clear();
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

                ResolveRecursively(key, scopedInstances: _instances, dependencyContainer: this);
            }
        }
    }

    private object ResolveRecursively(
        object key,
        Dictionary<object, object> scopedInstances,
        DependencyContainer dependencyContainer
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
                        dependencyContainer.CreateInstance(key, factory, cleanup),
                    TypeRegistration.Scoped { Factory: var factory, Cleanup: var cleanup } =>
                        dependencyContainer.GetOrAdd(scopedInstances, key, factory, cleanup),
                    TypeRegistration.Singleton { Factory: var factory, Cleanup: var cleanup } =>
                        GetOrAdd(_instances, key, factory, cleanup),
                    _ => throw new InvalidOperationException($"Not supported type registration: {typeRegistration}")
                };
            }

            if (_parent == null)
            {
                throw new InvalidOperationException($"Key not found: {key}");
            }

            return _parent.ResolveRecursively(key, scopedInstances, dependencyContainer);
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

        var newInstance = CreateInstance(key, factory, cleanup);

        instances.Add(key, newInstance);

        return newInstance;
    }

    private object CreateInstance(
        object key,
        DependencyFactory<object> factory,
        Action<object>? cleanup
    )
    {
        var cleanupCollector = new CleanupCollector();
        object instance;

        _creationDepth += 1;

        try
        {
            instance = factory.Invoke(this, key, ref cleanupCollector);

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

        _cleanups.Add(new CleanupEntry(instance, cleanup));

        foreach (var entry in cleanupCollector.Entries)
        {
            _cleanups.Add(entry);
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

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(objectName: nameof(DependencyContainer));
        }
    }
    #endregion
}

