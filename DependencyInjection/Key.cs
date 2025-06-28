namespace Macaron.DependencyInjection;

public static class Key
{
    #region Types
    private static class Cached<T>
    {
        public static readonly Key<T> Instance = new(Tag: "");
    }
    #endregion

    #region Static
    public static Key<T> Of<T>(string? tag)
    {
        return string.IsNullOrWhiteSpace(tag) ? Cached<T>.Instance : new Key<T>(tag.Trim());
    }

    public static Key<T> Of<T>()
    {
        return Cached<T>.Instance;
    }
    #endregion
}
