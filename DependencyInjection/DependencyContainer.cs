using System.Collections.Immutable;
using Macaron.DependencyInjection.Internal;

namespace Macaron.DependencyInjection;

public sealed class DependencyContainer(
    DependencyContainer? parent,
    ImmutableDictionary<(Type, object?), TypeRegistration> registry
) : IDependencyContainer
{
    #region Types
    internal readonly record struct Key(Type Type, object? Tag)
    {
        public override string ToString()
        {
            return Tag is null
                ? $"Key {{ Type = {Type} }}"
                : $"Key {{ Type = {Type}, Tag = {Tag} }}";
        }
    }
    #endregion

    #region Fields
    private readonly Dictionary<Key, object> _instances = new();
    private readonly List<CleanupEntry> _cleanups = [];
    private readonly Stack<Key> _resolutionStack = new();
    private int _creationDepth;
    private bool _isDisposed;

    private DependencyContainer? _parent = parent;
    private ImmutableDictionary<(Type, object?), TypeRegistration> _registry = registry;
    #endregion

    #region IDependencyContainer Interface
    object IDependencyResolver.Resolve(Type type, object? tag)
    {
        return ResolveRecursively(type, tag, scopedInstances: _instances, dependencyContainer: this);
    }

    bool IDependencyResolver.Contains(Type type, object? tag)
    {
        ThrowIfDisposed();

        return _registry.ContainsKey((type, tag)) || (_parent as IDependencyResolver)?.Contains(type, tag) is true;
    }

    public IDependencyContainer CreateScope(Action<IDependencyRegistrar>? configure)
    {
        ThrowIfDisposed();

        if (_creationDepth > 0)
        {
            throw new InvalidOperationException("Cannot call CreateScope while a service is being created.");
        }

        var builder = new DependencyContainerBuilder();

        configure?.Invoke(builder);

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
        _registry = ImmutableDictionary<(Type, object?), TypeRegistration>.Empty;

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
    private object ResolveRecursively(
        Type type,
        object? tag,
        Dictionary<Key, object> scopedInstances,
        DependencyContainer dependencyContainer
    )
    {
        ThrowIfDisposed();

        var key = new Key(type, tag);

        if (_resolutionStack.Contains(key))
        {
            throw new InvalidOperationException($"Cyclic dependency detected: {string.Join(" -> ", _resolutionStack.Reverse().Append(key))}");
        }

        _resolutionStack.Push(key);

        try
        {
            if (_registry.TryGetValue((type, tag), out var typeRegistration))
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

            return _parent.ResolveRecursively(type, tag, scopedInstances, dependencyContainer);
        }
        finally
        {
            _resolutionStack.Pop();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(objectName: nameof(DependencyContainer));
        }
    }

    private object GetOrAdd(
        Dictionary<Key, object> instances,
        Key key,
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
        Key key,
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
    #endregion
}

