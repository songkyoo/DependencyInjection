namespace Macaron.DependencyInjection.Internal;

internal static class Extensions
{
    public static object Invoke<T>(
        this Func<IDependencyContainer, T> factory,
        IDependencyContainer dependencyContainer,
        DependencyContainer.Key key
    ) where T : notnull
    {
        var instance = factory.Invoke(dependencyContainer);

        if (instance is null)
        {
            throw new InvalidOperationException($"Factory returned null for registration: {key}");
        }

        if (!key.Type.IsInstanceOfType(instance))
        {
            throw new InvalidOperationException($"{key.Type} is not assignable from {instance.GetType()}");
        }

        return instance;
    }
}
