namespace Redis;

public record StreamEntry(string Id, List<(string Field, string Value)> Fields);
