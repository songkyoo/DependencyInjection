namespace Macaron.DependencyInjection;

public readonly struct InjectionContext<T>
{
    internal InjectionContext(IDependencyResolver resolver)
    {
        Resolver = resolver;
    }

    public IDependencyResolver Resolver =>
        field ?? throw new InvalidOperationException("The injection context has not been initialized.");
}
