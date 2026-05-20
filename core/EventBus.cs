namespace Core;

public sealed class EventBus<T>
{
    private readonly List<IObserver<T>> _subs = new();

    public IDisposable Subscribe(IObserver<T> observer)
    {
        _subs.Add(observer);
        return new Unsub(_subs, observer);
    }

    public void Publish(T evt)
    {
        foreach (var s in _subs.ToArray())
            s.OnNext(evt);
    }

    private sealed class Unsub : IDisposable
    {
        private readonly List<IObserver<T>> _list;
        private readonly IObserver<T> _obs;
        public Unsub(List<IObserver<T>> list, IObserver<T> obs) { _list = list; _obs = obs; }
        public void Dispose() => _list.Remove(_obs);
    }
}
