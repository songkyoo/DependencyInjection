namespace Macaron.DependencyInjection;

public static class Key
{
    #region Types
    private static class Cached<T>
    {
        public static readonly Key<T> Instance = new();
    }
    #endregion

    #region Static
    public static Key<T> Of<T>(object? tag)
    {
        return tag is null ? Cached<T>.Instance : new Key<T>(tag);
    }

    public static Key<T> Of<T>()
    {
        return Cached<T>.Instance;
    }
    #endregion
}
