using System.Net;
using System.Text.Json;
using Azure;
using Cloud5mins.ShortenerTools;
using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Messages;
using Cloud5mins.ShortenerTools.Core.Service;
using Cloud5mins.ShortenerTools.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class ProductionBoundaryTests
{
    [Fact]
    public void Parser_preserves_update_presence_and_validates_legacy_entity_fields()
    {
        using var document = JsonDocument.Parse("""
            {"partitionKey":"a","rowKey":"abc","title":null,"data":{"x":"y"},"ShortUrl":"old","CreatedDate":"today","Clicks":2,"IsArchived":false,"Timestamp":"2025-01-02T03:04:05Z","ETag":"*","DataPropertyRaw":{"bad":true},"ActiveUrl":{"bad":true},"unknown":{"bad":true}}
            """);
        var command = ShortRequestParser.ParseUpdate(document);

        Assert.Equal("a", command.PartitionKey);
        Assert.Equal("abc", command.RowKey);
        Assert.True(command.Title.IsPresent);
        Assert.Null(command.Title.Value);
        Assert.True(command.Data.IsPresent);
        Assert.Equal("y", command.Data.Value["x"]);
        Assert.False(command.Url.IsPresent);
        Assert.False(command.Schedules.IsPresent);

        using var absent = JsonDocument.Parse("{}");
        Assert.False(ShortRequestParser.ParseUpdate(absent).Title.IsPresent);

        foreach (var body in new[]
        {
            "{\"data\":{},\"data\":{}}",
            "{\"Data\":{},\"dAtA\":{}}",
            "{\"ShortUrl\":1}",
            "{\"CreatedDate\":1}",
            "{\"Clicks\":\"2\"}",
            "{\"IsArchived\":\"false\"}",
            "{\"Timestamp\":\"not-a-date\"}",
            "{\"ETag\":123}"
        })
        {
            using var invalid = JsonDocument.Parse(body);
            Assert.Throws<JsonException>(() => ShortRequestParser.ParseUpdate(invalid));
        }

        using var create = JsonDocument.Parse("{\"vanity\":\"a\",\"data\":{\"x\":\"y\"}}");
        Assert.Equal("y", ShortRequestParser.ParseCreate(create).Data!["x"]);
    }

    [Fact]
    public async Task Parser_uses_ordinal_case_insensitive_names_without_claiming_unicode_lookalikes()
    {
        using var unknownUnicode = JsonDocument.Parse("{\"title\":\"after\",\"lin\\u212AType\":\"web\",\"clic\\u212As\":\"garbage\"}");
        var ignored = ShortRequestParser.ParseUpdate(unknownUnicode);
        Assert.True(ignored.Title.IsPresent);
        Assert.Equal("after", ignored.Title.Value);
        Assert.False(ignored.LinkType.IsPresent);

        using var mobile = JsonDocument.Parse("{\"partitionKey\":\"m\",\"rowKey\":\"mobile\",\"tItLe\":\"after\",\"LiNkTyPe\":\"mobile\",\"cLiCkS\":2}");
        var recognized = ShortRequestParser.ParseUpdate(mobile);
        Assert.True(recognized.Title.IsPresent);
        Assert.Equal("mobile", recognized.LinkType.Value);

        using var invalidAsciiClicks = JsonDocument.Parse("{\"cLiCkS\":\"garbage\"}");
        Assert.Throws<JsonException>(() => ShortRequestParser.ParseUpdate(invalidAsciiClicks));

        var storage = new StorageStub(Entity("mobile", LinkTypes.Mobile, "https://portal.example", "before"));
        var result = await Service(storage).Update(ignored, "host");
        Assert.Equal("after", result.Title);
        Assert.Equal(LinkTypes.Mobile, result.LinkType);
        Assert.Equal(1, storage.Reads);
        Assert.Equal(1, storage.Saves);
    }

    [Fact]
    public void Effective_schedules_follow_typed_then_raw_presence_precedence_and_defer_raw_parsing()
    {
        using var rawThenTyped = JsonDocument.Parse("{\"schedulesPropertyRaw\":\"not-json\",\"schedules\":[]}");
        Assert.Empty(ShortRequestParser.ParseUpdate(rawThenTyped).GetEffectiveSchedules());

        using var typedThenRaw = JsonDocument.Parse("{\"schedules\":[],\"schedulesPropertyRaw\":\"not-json\"}");
        Assert.Empty(ShortRequestParser.ParseUpdate(typedThenRaw).GetEffectiveSchedules());

        using var nullTyped = JsonDocument.Parse("{\"schedules\":null,\"schedulesPropertyRaw\":\"[{\\\"alternativeUrl\\\":\\\"https://example.com\\\"}]\"}");
        Assert.Single(ShortRequestParser.ParseUpdate(nullTyped).GetEffectiveSchedules());

        using var rawOnly = JsonDocument.Parse("{\"schedulesPropertyRaw\":\"not-json\"}");
        var deferred = ShortRequestParser.ParseUpdate(rawOnly);
        Assert.Throws<JsonException>(() => deferred.GetEffectiveSchedules());

        using var rawNull = JsonDocument.Parse("{\"schedulesPropertyRaw\":\"null\"}");
        Assert.Throws<ArgumentNullException>(() => ShortRequestParser.ParseUpdate(rawNull).GetEffectiveSchedules());

        using var malformedTyped = JsonDocument.Parse("{\"schedules\":[1],\"schedulesPropertyRaw\":\"[]\"}");
        Assert.Throws<JsonException>(() => ShortRequestParser.ParseUpdate(malformedTyped));
    }

    [Fact]
    public async Task Update_reads_and_saves_once_for_web_and_mobile_and_projects_urls()
    {
        const string host = "https://short.example";
        var web = Entity("%41", "web", "https://old.example", "before");
        web.SchedulesPropertyRaw = "[]";
        var webStorage = new StorageStub(web);
        using var webRequest = JsonDocument.Parse("{\"partitionKey\":\"%\",\"rowKey\":\"%41\",\"url\":\"https://new.example\",\"schedules\":[]}");
        var webResult = await Service(webStorage).Update(ShortRequestParser.ParseUpdate(webRequest), host);
        Assert.Equal(1, webStorage.Reads);
        Assert.Equal(1, webStorage.Saves);
        Assert.Equal("https://new.example", webResult.Url);
        Assert.Null(webResult.Title);
        Assert.Equal("[]", webResult.SchedulesPropertyRaw);
        Assert.Equal("https://short.example/%41", webResult.ShortUrl);

        var mobile = Entity("%41", LinkTypes.Mobile, "https://portal.example", "before");
        mobile.Data = new Dictionary<string, string> { ["old"] = "value" };
        var mobileStorage = new StorageStub(mobile);
        using var mobileRequest = JsonDocument.Parse("{\"partitionKey\":\"%\",\"rowKey\":\"%41\",\"title\":\"after\",\"data\":{\"new\":\"value\"}}");
        var mobileResult = await Service(mobileStorage).Update(ShortRequestParser.ParseUpdate(mobileRequest), host);
        Assert.Equal(1, mobileStorage.Reads);
        Assert.Equal(1, mobileStorage.Saves);
        Assert.Equal("after", mobileResult.Title);
        Assert.Equal("value", mobileResult.Data["new"]);
        Assert.Equal("https://short.example/m/%2541", mobileResult.ShortUrl);
        Assert.Equal("https://short.example/m/%2541", Utility.GetShortUrl(host, mobile));
    }

    [Fact]
    public async Task Update_rejection_and_validation_precedence_do_not_save()
    {
        var mobileStorage = new StorageStub(Entity("mobile", LinkTypes.Mobile, "https://portal.example", "before"));
        using var mobileSchedule = JsonDocument.Parse("{\"partitionKey\":\"m\",\"rowKey\":\"mobile\",\"schedulesPropertyRaw\":\"[{\\\"alternativeUrl\\\":\\\"https://x.example\\\"}]\"}");
        var rejected = await Assert.ThrowsAsync<ShortenerToolException>(() => Service(mobileStorage).Update(ShortRequestParser.ParseUpdate(mobileSchedule), "host"));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(1, mobileStorage.Reads);
        Assert.Equal(0, mobileStorage.Saves);

        var missingStorage = new StorageStub(null) { ReadException = new InvalidOperationException("missing") };
        using var malformedRaw = JsonDocument.Parse("{\"partitionKey\":\"x\",\"rowKey\":\"missing\",\"schedulesPropertyRaw\":\"not-json\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(missingStorage).Update(ShortRequestParser.ParseUpdate(malformedRaw), "host"));
        Assert.Equal(1, missingStorage.Reads);
        Assert.Equal(0, missingStorage.Saves);

        using var malformedTyped = JsonDocument.Parse("{\"partitionKey\":\"x\",\"rowKey\":\"missing\",\"schedules\":[1]}");
        Assert.Throws<JsonException>(() => ShortRequestParser.ParseUpdate(malformedTyped));
        Assert.Equal(1, missingStorage.Reads);

        var webStorage = new StorageStub(Entity("web", LinkTypes.Web, "https://old.example", "before"));
        using var invalidWebUrl = JsonDocument.Parse("{\"partitionKey\":\"w\",\"rowKey\":\"web\",\"url\":\"not-a-url\",\"schedulesPropertyRaw\":\"not-json\"}");
        var invalidUrl = await Assert.ThrowsAsync<ShortenerToolException>(() => Service(webStorage).Update(ShortRequestParser.ParseUpdate(invalidWebUrl), "host"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUrl.StatusCode);
        Assert.Equal(1, webStorage.Reads);
        Assert.Equal(0, webStorage.Saves);
    }

    [Fact]
    public void Entity_metadata_reads_are_snapshots_and_assignment_replaces_raw_value()
    {
        var entity = new ShortUrlEntity { DataPropertyRaw = "{\"screen\":\"home\"}" };
        var first = entity.Data;
        first["screen"] = "changed";
        Assert.Equal("home", entity.Data["screen"]);

        var replacement = new Dictionary<string, string> { ["screen"] = "new" };
        entity.Data = replacement;
        replacement["screen"] = "mutated-after-assignment";
        Assert.Equal("new", entity.Data["screen"]);
        Assert.Equal("{\"screen\":\"new\"}", entity.DataPropertyRaw);

        foreach (var raw in new string?[] { null, "", "null" })
        {
            var empty = new ShortUrlEntity { DataPropertyRaw = raw };
            Assert.Empty(empty.Data);
        }
    }

    private static UrlServices Service(StorageStub storage) => new(NullLogger<UrlServices>.Instance, storage);

    private static ShortUrlEntity Entity(string rowKey, string linkType, string url, string title) => new()
    {
        PartitionKey = rowKey[..1],
        RowKey = rowKey,
        LinkType = linkType,
        Url = url,
        Title = title,
        DataPropertyRaw = "{}",
        SchedulesPropertyRaw = "[]"
    };

    private sealed class StorageStub(ShortUrlEntity? entity) : IAzStrorageTablesService
    {
        public int Reads { get; private set; }
        public int Saves { get; private set; }
        public Exception? ReadException { get; init; }

        public Task<ShortUrlEntity> GetShortUrlEntity(ShortUrlEntity row)
        {
            Reads++;
            if (ReadException != null)
            {
                return Task.FromException<ShortUrlEntity>(ReadException);
            }

            return entity == null
                ? Task.FromException<ShortUrlEntity>(new InvalidOperationException("missing"))
                : Task.FromResult(entity);
        }

        public Task<ShortUrlEntity> SaveShortUrlEntity(ShortUrlEntity updated)
        {
            Saves++;
            entity = updated;
            return Task.FromResult(updated);
        }

        public Task<int> GetNextTableId() => throw new NotSupportedException();
        public Task<List<ShortUrlEntity>> GetAllShortUrlEntities() => throw new NotSupportedException();
        public Task<bool> IfShortUrlEntityExist(ShortUrlEntity row) => throw new NotSupportedException();
        public Task<ShortUrlEntity?> GetShortUrlEntityByVanity(string vanity) => throw new NotSupportedException();
        public Task<bool> IfShortUrlEntityExistByVanity(string vanity) => throw new NotSupportedException();
        public Task<ShortUrlEntity> ArchiveShortUrlEntity(ShortUrlEntity urlEntity) => throw new NotSupportedException();
        public Task<List<ClickStatsEntity>> GetAllStatsByVanity(string vanity, string? startDate = null, string? endDate = null) => throw new NotSupportedException();
        public Task SaveClickStatsEntity(ClickStatsEntity newStats) => throw new NotSupportedException();
        public Task ImportUrlDataAsync(UrlDetails urlData) => throw new NotSupportedException();
        public Task ImportClickStatsAsync(List<ClickStatsEntity> lstClickStats) => throw new NotSupportedException();
    }
}
