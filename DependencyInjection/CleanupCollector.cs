namespace Macaron.DependencyInjection;

public struct CleanupCollector
{
    #region Fields
    private CleanupEntry[]? _entries;
    private int _count;
    #endregion

    #region Properties
    internal readonly ReadOnlySpan<CleanupEntry> Entries => _entries.AsSpan(start: 0, length: _count);
    #endregion

    #region Methods
    public void Add(IDisposable instance)
    {
        Add(instance, cleanup: null);
    }

    public void Add(object instance, Action<object>? cleanup)
    {
        if (_entries is null)
        {
            _entries = new CleanupEntry[4];
        }
        else if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, newSize: _entries.Length * 2);
        }

        _entries[_count++] = new CleanupEntry(instance, cleanup);
    }
    #endregion
}
