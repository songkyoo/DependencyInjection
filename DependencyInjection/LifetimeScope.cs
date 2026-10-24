namespace Macaron.DependencyInjection;

public sealed class LifetimeScope : IDependencyResolver, IDisposable
{
    #region Fields
    private DependencyContainer? _container;
    private readonly int _index;
    #endregion

    #region Properties
    internal int Index => _index;

    private DependencyContainer Container => _container ?? throw new ObjectDisposedException(nameof(LifetimeScope));
    #endregion

    #region Constructors
    internal LifetimeScope(DependencyContainer container, int index)
    {
        _container = container;
        _index = index;
    }
    #endregion

    #region IDependencyResolver Interface
    public T Resolve<T>(Key<T> key)
    {
        return Container.Resolve(key, lifetimeScope: this);
    }

    public bool Contains<T>(Key<T> key)
    {
        return Container.Contains(key, lifetimeScope: this);
    }
    #endregion

    #region IDisposable Interface
    public void Dispose()
    {
        _container?.DisposeLifetimeScope(this);
    }
    #endregion

    #region Methods
    internal void Invalidate()
    {
        _container = null;
    }
    #endregion
}
