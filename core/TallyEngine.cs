namespace Core;

public sealed class TallyEngine
{
    public static IEnumerable<TallyUpdate> Compute(SwitcherState s)
    {
        var map = s.Inputs.ToDictionary(i => i.Id, i => i.Alias);
        foreach (var me in s.MEs)
        {
            foreach (var kv in map)
            {
                yield return new TallyUpdate(
                    kv.Value,
                    me.MeIndex,
                    Program: me.Program.Contains(kv.Key),
                    Preview: me.Preview.Contains(kv.Key)
                );
            }
        }
    }
}
