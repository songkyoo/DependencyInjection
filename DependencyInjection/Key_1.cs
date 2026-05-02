namespace Macaron.DependencyInjection;

public sealed record Key<T> : IKey
{
    #region Constructors
    public Key(object? tag = null)
    {
        Tag = tag;
    }
    #endregion

    #region Overrides
    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Tag);
    }

    public override string ToString()
    {
        return Tag is null
            ? $"Key {{ Type = {typeof(T)} }}"
            : $"Key {{ Type = {typeof(T)}, Tag = {Tag} }}";
    }
    #endregion

    #region IKey Interface
    public Type Type => typeof(T);

    public object? Tag { get; }

    public bool Equals(IKey? other)
    {
        return other is not null && other.Type == typeof(T) && Equals(other.Tag, Tag);
    }
    #endregion

    #region Methods
    public void Deconstruct(out Type type, out object? tag)
    {
        type = Type;
        tag = Tag;
    }
    #endregion
}
