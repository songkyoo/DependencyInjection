namespace Macaron.DependencyInjection;

public sealed record Key<T>(
    string Tag
) : IKey
{
    #region Inheritance
    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Tag);
    }

    public override string ToString()
    {
        return $"Key {{ Type = {typeof(T)}, Tag = {Tag}) }}";
    }
    #endregion

    #region IKey Interface
    public Type Type => typeof(T);

    public bool Equals(IKey? other)
    {
        return other is Key<T> key && key.Tag == Tag;
    }
    #endregion
}
