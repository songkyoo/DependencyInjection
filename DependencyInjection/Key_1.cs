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
            ? $"Key {{ Type = {typeof(T)}, Id = {Id} }}"
            : $"Key {{ Type = {typeof(T)}, Name = {Name}, Id = {Id} }}";
    }
    #endregion

    #region Properties
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; } = name;
    #endregion
}
