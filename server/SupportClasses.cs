using Core;

public sealed class TallyBus
{
    private readonly List<Func<string, Task>> _subs = new();

    public IDisposable Subscribe(Func<string, Task> cb)
    {
        _subs.Add(cb);
        return new Unsub(_subs, cb);
    }

    public Task PublishAsync(string json) => Task.WhenAll(_subs.Select(s => s(json)));

    private sealed class Unsub : IDisposable
    {
        private readonly List<Func<string, Task>> _subs;
        private readonly Func<string, Task> _cb;
        public Unsub(List<Func<string, Task>> subs, Func<string, Task> cb) { _subs = subs; _cb = cb; }
        public void Dispose() => _subs.Remove(_cb);
    }
}

public sealed class VenueStore
{
    public VenueMap Map { get; set; } = new(1920, 1080, new());
}

public sealed class ShotBus
{
    public EventBus<ShotCue> Bus { get; } = new();
}
