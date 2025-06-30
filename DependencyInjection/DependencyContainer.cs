using System.Collections.Immutable;
using Macaron.DependencyInjection.Internal;

namespace Macaron.DependencyInjection;

public sealed class DependencyContainer(
    DependencyContainer? parent,
    ImmutableDictionary<(Type, string), TypeRegistration> registry
) : IDependencyResolver, IDisposable
{
    #region Fields
    private readonly Dictionary<(Type, string), object> _instances = new();
    private readonly Stack<(Type, string)> _resolutionStack = new();
    private bool _isDisposed;
    #endregion

    #region IDependencyResolver Interface
    object IDependencyResolver.Resolve(Type type, string tag)
    {
        return ResolveRecursively((type, tag), scopedInstances: _instances, dependencyResolver: this);
    }

    bool IDependencyResolver.Contains(Type type, string tag)
    {
        ThrowIfDisposed();

        return registry.ContainsKey((type, tag)) || (parent as IDependencyResolver)?.Contains(type, tag) is true;
    }
    #endregion

    #region IDisposable Interface
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (var instance in _instances.Values)
        {
            if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _instances.Clear();
    }
    #endregion

    #region Methods
    private object ResolveRecursively(
        (Type, string) key,
        Dictionary<(Type, string), object> scopedInstances,
        IDependencyResolver dependencyResolver
    )
    {
        ThrowIfDisposed();

        if (_resolutionStack.Contains(key))
        {
            throw new InvalidOperationException($"Cyclic dependency detected: {string.Join(" -> ", _resolutionStack.Append(key))}");
        }

        _resolutionStack.Push(key);

        try
        {
            if (registry.TryGetValue(key, out var typeRegistration))
            {
                var (lifeTime, factory) = typeRegistration;

                return lifeTime switch
                {
                    LifeTime.Transient => factory.Invoke(dependencyResolver, key.Item1),
                    LifeTime.Scoped => scopedInstances.GetOrAdd(key, dependencyResolver, factory),
                    LifeTime.Singleton => _instances.GetOrAdd(key, dependencyResolver: this, factory),
                    _ => throw new InvalidOperationException($"Not supported life time: {typeRegistration.LifeTime}"),
                };
            }

            if (parent == null)
            {
                throw new InvalidOperationException($"Key not found: {key}");
            }

            return parent.ResolveRecursively(key, scopedInstances, dependencyResolver);
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
        this Dictionary<(Type, string), object> instances,
        (Type, string) key,
        IDependencyResolver dependencyResolver,
        Func<IDependencyResolver, object> factory
    )
    {
        if (instances.TryGetValue(key, out var instance))
        {
            return instance;
        }

        var newInstance = factory.Invoke(dependencyResolver, key.Item1);
        instances.Add(key, newInstance);

        return newInstance;
    }
}

