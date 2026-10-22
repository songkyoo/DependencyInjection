namespace Macaron.DependencyInjection.Internal;

internal readonly record struct CleanupEntry(object Instance, Action<object>? Cleanup)
{
    public void Invoke()
    {
        if (Cleanup is { } cleanup)
        {
            cleanup(Instance);
        }
        else
        {
            (Instance as IDisposable)?.Dispose();
        }
    }
}
