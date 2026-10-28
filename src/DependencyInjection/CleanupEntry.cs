namespace Macaron.DependencyInjection;

internal readonly record struct CleanupEntry(object Instance, Action<object>? Cleanup)
{
    internal void Invoke()
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
