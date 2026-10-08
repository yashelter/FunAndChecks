namespace Frontend.Shared.Services;

/// <summary>Tracks edits that would be lost by the culture-switch reload.</summary>
public sealed class UnsavedChangesTracker
{
    private static readonly Guid GlobalSource = Guid.Empty;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _dirtySources = new();

    public bool IsDirty => !_dirtySources.IsEmpty;
    public void MarkDirty() => _dirtySources[GlobalSource] = 0;
    public void MarkClean() => _dirtySources.TryRemove(GlobalSource, out _);
    public void ClearAll() => _dirtySources.Clear();
    public Registration Register() => new(this, Guid.NewGuid());

    public sealed class Registration : IDisposable
    {
        private readonly UnsavedChangesTracker _owner;
        private readonly Guid _id;

        internal Registration(UnsavedChangesTracker owner, Guid id)
        {
            _owner = owner;
            _id = id;
        }

        public void MarkDirty() => _owner._dirtySources[_id] = 0;
        public void MarkClean() => _owner._dirtySources.TryRemove(_id, out _);
        public void Dispose() => MarkClean();
    }
}
