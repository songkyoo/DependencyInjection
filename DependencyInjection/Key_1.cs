namespace Macaron.DependencyInjection;

public sealed class Key<T>(string name = "")
{
    #region Static
    public static Key<T> Default { get; } = new();
    #endregion

    #region Overrides
    public override string ToString()
    {
        return Name.Length == 0
            ? $"Key {{ Type = {typeof(T)} }}"
            : $"Key {{ Type = {typeof(T)}, Name = {Name} }}";
    }
    #endregion

    #region Properties
    public string Name { get; } = name;
    #endregion
}
