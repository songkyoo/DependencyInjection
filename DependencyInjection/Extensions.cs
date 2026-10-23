namespace Macaron.DependencyInjection;

internal static class Extensions
{
    public static object Invoke<T>(
        this DependencyFactory<T> factory,
        IDependencyContainer dependencyContainer,
        object key,
        ref CleanupCollector cleanupCollector
    ) where T : notnull
    {
        var instance = factory.Invoke(dependencyContainer, ref cleanupCollector);

        if (instance is null)
        {
            throw new InvalidOperationException($"Factory returned null for registration: {key}");
        }

        return instance;
    }
}
