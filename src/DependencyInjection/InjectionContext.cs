namespace Macaron.DependencyInjection;

public readonly struct InjectionContext<T>
{
    private readonly IDependencyResolver? _resolver;

    internal InjectionContext(IDependencyResolver resolver)
    {
        _resolver = resolver;
    }

    public IDependencyResolver Resolver => _resolver
        ?? throw new InvalidOperationException("The injection context has not been initialized.");
}
