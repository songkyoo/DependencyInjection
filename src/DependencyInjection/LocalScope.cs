namespace Macaron.DependencyInjection;

public sealed class LocalScope : IDependencyResolver, IDisposable
{
    #region Fields
    private DependencyContainer? _container;
    private readonly int _index;
    #endregion

    #region Properties
    internal int Index => _index;

    private DependencyContainer Container => _container ?? throw new ObjectDisposedException(nameof(LocalScope));
    #endregion

    #region Constructors
    internal LocalScope(DependencyContainer container, int index)
    {
        _container = container;
        _index = index;
    }
    #endregion

    #region IDependencyResolver Interface
    public T Resolve<T>(object? tag = null)
    {
        return Container.Resolve<T>(tag, localScope: this);
    }

    public bool Contains<T>(object? tag = null)
    {
        return Container.Contains<T>(tag, localScope: this);
    }
    #endregion

    #region IDisposable Interface
    public void Dispose()
    {
        _container?.DisposeLocalScope(this);
    }
    #endregion

    #region Methods
    internal void Invalidate()
    {
        _container = null;
    }
    #endregion
}
