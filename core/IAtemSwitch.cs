namespace Core;

public interface IAtemSwitch
{
    Task ConnectAsync(string host);
    Task<SwitcherState> GetStateAsync();
    IAsyncEnumerable<SwitcherState> StateStream(CancellationToken ct);
    Task CutAsync(int meIndex, long inputId);
    Task MixAsync(int meIndex, long inputId, double rate);
    Task SetPreviewAsync(int meIndex, long inputId);
}
