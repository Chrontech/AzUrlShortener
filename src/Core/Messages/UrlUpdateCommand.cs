using Cloud5mins.ShortenerTools.Core.Domain;

namespace Cloud5mins.ShortenerTools.Core.Messages;

public readonly struct Supplied<T>
{
    public bool IsPresent { get; }
    public T Value { get; }

    public Supplied(T value)
    {
        IsPresent = true;
        Value = value;
    }
}

public sealed class UrlUpdateCommand
{
    public string? PartitionKey { get; internal set; }
    public string? RowKey { get; internal set; }
    public Supplied<string?> Title { get; internal set; }
    public Supplied<string?> Url { get; internal set; }
    public Supplied<string?> LinkType { get; internal set; }
    public Supplied<Dictionary<string, string>> Data { get; internal set; }
    public Supplied<List<Schedule>?> Schedules { get; internal set; }
    public Supplied<string?> SchedulesPropertyRaw { get; internal set; }

    public List<Schedule> GetEffectiveSchedules()
    {
        if (Schedules.IsPresent && Schedules.Value != null)
        {
            return Schedules.Value;
        }

        var raw = SchedulesPropertyRaw.IsPresent ? SchedulesPropertyRaw.Value : null;
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        return System.Text.Json.JsonSerializer.Deserialize<Schedule[]>(raw)!.ToList();
    }
}
