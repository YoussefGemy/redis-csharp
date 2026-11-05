namespace Redis;

public record StreamEntry(long Ms, long Seq, List<(string Field, string Value)> Fields)
{
    public string Id => $"{Ms}-{Seq}";
}
