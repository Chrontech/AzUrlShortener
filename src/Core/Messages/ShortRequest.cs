using Cloud5mins.ShortenerTools.Core.Domain;

namespace Cloud5mins.ShortenerTools.Core.Messages
{
    public class ShortRequest
    {
        public string Vanity { get; set; }

        public string? Url { get; set; }

        public string Title { get; set; }

        public Schedule[]? Schedules { get; set; }

        public string? LinkType { get; set; }

        public Dictionary<string, string>? Data { get; set; }
    }
}
