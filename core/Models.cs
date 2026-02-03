namespace Core;

public sealed record SwitcherInput(long Id, string Name, string Alias);
public sealed record MEState(int MeIndex, HashSet<long> Program, HashSet<long> Preview);
public sealed record SwitcherState(List<SwitcherInput> Inputs, List<MEState> MEs);

public sealed record TallyUpdate(string CamAlias, int MeIndex, bool Program, bool Preview);

public sealed record VenueItem(string Type, string Alias, double XNorm, double YNorm, double FovDeg);
public sealed record VenueMap(int Width, int Height, List<VenueItem> Items);

public sealed record ShotCue(string CamAlias, string Title, string Details, int Priority = 1);
