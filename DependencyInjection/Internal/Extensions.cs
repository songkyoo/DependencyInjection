namespace Macaron.DependencyInjection.Internal;

internal static class Extensions
{
    public static object Invoke<T>(
        this Func<IDependencyResolver, T> factory,
        IDependencyResolver dependencyResolver,
        Type type
    ) where T : notnull
    {
        var instance = factory.Invoke(dependencyResolver);

        if (!type.IsAssignableFrom(instance.GetType()))
        {
            throw new InvalidOperationException($"{type} is not assignable from {instance.GetType()}");
        }

        return instance;
    }
}
