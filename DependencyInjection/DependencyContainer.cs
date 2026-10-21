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
    private readonly List<IDisposable> _disposables = [];
    private readonly Stack<Key> _resolutionStack = new();
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
        var builder = new DependencyContainerBuilder();

        configure?.Invoke(builder);

        return builder.Build(parent: this);
    }

    public void AddDisposable(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _parent = null;
        _registry = ImmutableDictionary<(Type, object?), TypeRegistration>.Empty;

        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _disposables.Clear();
        _instances.Clear();
    }
    #endregion

    #region Methods
    private object ResolveRecursively(
        Type type,
        object? tag,
        Dictionary<Key, object> scopedInstances,
        IDependencyContainer dependencyContainer
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
                    TypeRegistration.Transient { Factory: var factory } =>
                        factory.Invoke(dependencyContainer, key),
                    TypeRegistration.Scoped { Factory: var factory } =>
                        scopedInstances.GetOrAdd(type, tag, dependencyContainer, factory, externallyOwned: false),
                    TypeRegistration.Singleton { Factory: var factory, ExternallyOwned: var externallyOwned } =>
                        _instances.GetOrAdd(type, tag, dependencyContainer: this, factory, externallyOwned),
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
    #endregion
}

file static class FileScopeExtensions
{
    public static object GetOrAdd(
        this Dictionary<DependencyContainer.Key, object> instances,
        Type type,
        object? tag,
        IDependencyContainer dependencyContainer,
        Func<IDependencyContainer, object> factory,
        bool externallyOwned
    )
    {
        var key = new DependencyContainer.Key(type, tag);

        if (instances.TryGetValue(key, out var instance))
        {
            return instance;
        }

        var newInstance = factory.Invoke(dependencyContainer, key);

        instances.Add(key, newInstance);

        if (!externallyOwned && newInstance is IDisposable disposable)
        {
            dependencyContainer.AddDisposable(disposable);
        }

        return newInstance;
    }
}

