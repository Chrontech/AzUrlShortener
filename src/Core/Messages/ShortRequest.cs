using Cloud5mins.ShortenerTools.Core.Domain;
using System.Text.Json.Serialization;

namespace Cloud5mins.ShortenerTools.Core.Messages
{
    public class ShortRequest
    {
        public string Vanity { get; set; }

        public string? Url { get; set; }

        public string Title { get; set; }

        public Schedule[]? Schedules { get; set; }

        public string? LinkType { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Data { get; set; }
    }
}
