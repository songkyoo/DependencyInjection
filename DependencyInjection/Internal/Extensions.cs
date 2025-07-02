namespace Macaron.DependencyInjection.Internal;

internal static class Extensions
{
    public static object Invoke<T>(
        this Func<IDependencyContainer, T> factory,
        IDependencyContainer dependencyContainer,
        Type type
    ) where T : notnull
    {
        var instance = factory.Invoke(dependencyContainer);

        if (!type.IsInstanceOfType(instance))
        {
            throw new InvalidOperationException($"{type} is not assignable from {instance.GetType()}");
        }

        return instance;
    }
}
