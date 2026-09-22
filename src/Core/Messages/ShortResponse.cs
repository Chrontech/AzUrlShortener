using Cloud5mins.ShortenerTools.Core.Domain;

namespace Cloud5mins.ShortenerTools.Core.Messages
{
    public class ShortResponse
    {
        public string ShortUrl { get; set; }
        public string LongUrl { get; set; }
        public string Title { get; set; }
        public string LinkType { get; set; }
        public Dictionary<string, string> Data { get; set; }

        public ShortResponse() { }
        public ShortResponse(string host, string longUrl, string endUrl, string title)
        {
            LongUrl = longUrl;
            ShortUrl = string.Concat(host, "/", endUrl);
            Title = title;
            LinkType = LinkTypes.Web;
            Data = new Dictionary<string, string>();

        }
    }
}
