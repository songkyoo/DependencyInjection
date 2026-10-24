namespace Macaron.DependencyInjection;

public sealed class TransientScope : IDependencyResolver, IDisposable
{
    #region Fields
    private DependencyContainer? _container;
    private readonly int _index;
    #endregion

    #region Properties
    internal int Index => _index;

    private DependencyContainer Container => _container ?? throw new ObjectDisposedException(nameof(TransientScope));
    #endregion

    #region Constructors
    internal TransientScope(DependencyContainer container, int index)
    {
        _container = container;
        _index = index;
    }
    #endregion

    #region IDependencyResolver Interface
    public T Resolve<T>(Key<T> key)
    {
        return Container.Resolve(key, transientScope: this);
    }

    public bool Contains<T>(Key<T> key)
    {
        return Container.Contains(key, transientScope: this);
    }
    #endregion

    #region IDisposable Interface
    public void Dispose()
    {
        _container?.DisposeTransientScope(this);
    }
    #endregion

    #region Methods
    internal void Invalidate()
    {
        _container = null;
    }
    #endregion
}
