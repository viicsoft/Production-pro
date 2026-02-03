using Core;
using System.Runtime.CompilerServices;

namespace Simulator;

/// <summary>
/// Simple 1 M/E simulator with 8 inputs (Cam1..Cam8).
/// Program starts on Cam1, Preview on Cam2. CUT/MIX put an input on Program.
/// Matches Core.Models MEState signature: MEState(int MeIndex, HashSet<long> Program, HashSet<long> Preview)
/// </summary>
public sealed class SimAtem : IAtemSwitch
{
    private readonly List<SwitcherInput> _inputs = new();
    private readonly List<MEState> _mes = new();
    private volatile bool _running;

    public Task ConnectAsync(string host)
    {
        // Fake inputs Cam1..Cam8
        _inputs.Clear();
        for (int i = 1; i <= 8; i++)
            _inputs.Add(new SwitcherInput(i, $"Input{i}", $"Cam{i}"));

        // Single M/E (index 0). Start with PGM=Cam1, PVW=Cam2
        _mes.Clear();
        _mes.Add(new MEState(
            MeIndex: 0,
            Program: new HashSet<long> { 1 },
            Preview: new HashSet<long> { 2 }
        ));

        _running = true;
        return Task.CompletedTask;
    }

    public Task<SwitcherState> GetStateAsync()
        => Task.FromResult(new SwitcherState(_inputs, _mes));

    public async IAsyncEnumerable<SwitcherState> StateStream([EnumeratorCancellation] CancellationToken ct)
    {
        // Send a heartbeat state every 500 ms
        while (!ct.IsCancellationRequested && _running)
        {
            yield return await GetStateAsync();
            try { await Task.Delay(500, ct); } catch { break; }
        }
    }

    public Task CutAsync(int meIndex, long inputId)
    {
        var me = _mes.First(m => m.MeIndex == meIndex);
        me.Program.Clear();
        me.Program.Add(inputId);
        return Task.CompletedTask;
    }

    public Task MixAsync(int meIndex, long inputId, double rate)
    {
        // For the simulator, MIX behaves like CUT (instant switch)
        return CutAsync(meIndex, inputId);
    }

    public Task SetPreviewAsync(int meIndex, long inputId)
    {
        var me = _mes.First(m => m.MeIndex == meIndex);
        me.Preview.Clear();
        me.Preview.Add(inputId);
        return Task.CompletedTask;
    }
}
