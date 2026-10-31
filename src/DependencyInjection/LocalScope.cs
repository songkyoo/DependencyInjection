namespace Macaron.DependencyInjection;

public sealed class LocalScope : IDependencyResolver, IDisposable
{
    #region Fields
    private DependencyContainer? _container;
    #endregion

    #region Properties
    internal int LastCleanupIndex { get; set; } = -1;

    private DependencyContainer Container => _container ?? throw new ObjectDisposedException(nameof(LocalScope));
    #endregion

    #region Constructors
    internal LocalScope(DependencyContainer container)
    {
        _container = container;
    }
    #endregion

    #region IDependencyResolver Interface
    public T Resolve<T>(object? tag = null)
    {
        return Container.Resolve<T>(tag, localScope: this);
    }

    public bool Contains<T>(object? tag = null)
    {
        return Container.Contains<T>(tag);
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
        LastCleanupIndex = -1;
    }
    #endregion
}
