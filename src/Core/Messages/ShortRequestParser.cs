using Azure;
using Cloud5mins.ShortenerTools.Core.Domain;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Core.Messages;

public static class ShortRequestParser
{
    private static readonly JsonSerializerOptions RequestOptions = new() { PropertyNameCaseInsensitive = true };

    public static ShortRequest ParseCreate(JsonDocument document)
    {
        ValidateBodyAndData(document.RootElement);
        return document.RootElement.Deserialize<ShortRequest>(RequestOptions)
            ?? throw new JsonException("The request body is required.");
    }

    public static UrlUpdateCommand ParseUpdate(JsonDocument document)
    {
        var body = document.RootElement;
        ValidateBodyAndData(body);

        var command = new UrlUpdateCommand();
        foreach (var property in body.EnumerateObject())
        {
            switch (property.Name)
            {
                case var partitionKey when string.Equals(partitionKey, "partitionkey", StringComparison.OrdinalIgnoreCase):
                    command.PartitionKey = Deserialize<string?>(property.Value);
                    break;
                case var rowKey when string.Equals(rowKey, "rowkey", StringComparison.OrdinalIgnoreCase):
                    command.RowKey = Deserialize<string?>(property.Value);
                    break;
                case var title when string.Equals(title, "title", StringComparison.OrdinalIgnoreCase):
                    command.Title = new Supplied<string?>(Deserialize<string?>(property.Value));
                    break;
                case var url when string.Equals(url, "url", StringComparison.OrdinalIgnoreCase):
                    command.Url = new Supplied<string?>(Deserialize<string?>(property.Value));
                    break;
                case var linkType when string.Equals(linkType, "linktype", StringComparison.OrdinalIgnoreCase):
                    command.LinkType = new Supplied<string?>(Deserialize<string?>(property.Value));
                    break;
                case var data when string.Equals(data, "data", StringComparison.OrdinalIgnoreCase):
                    command.Data = new Supplied<Dictionary<string, string>>(Deserialize<Dictionary<string, string>>(property.Value)!);
                    break;
                case var schedules when string.Equals(schedules, "schedules", StringComparison.OrdinalIgnoreCase):
                    command.Schedules = new Supplied<List<Schedule>?>(Deserialize<List<Schedule>?>(property.Value));
                    break;
                case var schedulesPropertyRaw when string.Equals(schedulesPropertyRaw, "schedulespropertyraw", StringComparison.OrdinalIgnoreCase):
                    command.SchedulesPropertyRaw = new Supplied<string?>(Deserialize<string?>(property.Value));
                    break;
                case var shortUrl when string.Equals(shortUrl, "shorturl", StringComparison.OrdinalIgnoreCase):
                    _ = Deserialize<string?>(property.Value);
                    break;
                case var createdDate when string.Equals(createdDate, "createddate", StringComparison.OrdinalIgnoreCase):
                    _ = Deserialize<string?>(property.Value);
                    break;
                case var clicks when string.Equals(clicks, "clicks", StringComparison.OrdinalIgnoreCase):
                    _ = Deserialize<int>(property.Value);
                    break;
                case var isArchived when string.Equals(isArchived, "isarchived", StringComparison.OrdinalIgnoreCase):
                    _ = Deserialize<bool?>(property.Value);
                    break;
                case var timestamp when string.Equals(timestamp, "timestamp", StringComparison.OrdinalIgnoreCase):
                    _ = Deserialize<DateTimeOffset?>(property.Value);
                    break;
                case var etag when string.Equals(etag, "etag", StringComparison.OrdinalIgnoreCase):
                    _ = Deserialize<ETag>(property.Value);
                    break;
            }
        }

        return command;
    }

    private static T Deserialize<T>(JsonElement value)
    {
        return value.Deserialize<T>(RequestOptions)!;
    }

    private static void ValidateBodyAndData(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The request body must be a JSON object.");
        }

        var dataFound = false;
        JsonElement data = default;
        foreach (var property in body.EnumerateObject())
        {
            if (!string.Equals(property.Name, "data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (dataFound)
            {
                throw new JsonException("The data parameter must not be specified more than once.");
            }

            dataFound = true;
            data = property.Value;
        }

        if (dataFound && (data.ValueKind != JsonValueKind.Object ||
            data.EnumerateObject().Any(item => item.Value.ValueKind != JsonValueKind.String)))
        {
            throw new JsonException("The data parameter must be an object with string values.");
        }
    }
}
