using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Messages;
using Cloud5mins.ShortenerTools.Core.Service;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Cloud5mins.ShortenerTools.Core.Services;

public class UrlServices
{
    private const string DefaultMobilePortalUrl = "https://portal.gochronicle.com/?site=download-chronicle%2F";
	private readonly ILogger _logger;
	private readonly IAzStrorageTablesService _stgHelper;

	public UrlServices(ILogger logger, IAzStrorageTablesService stgHelper)
	{
		_logger = logger;
		_stgHelper = stgHelper;
	}

	public async Task<ShortUrlEntity> Archive(ShortUrlEntity input)
	{
		ShortUrlEntity result = await _stgHelper.ArchiveShortUrlEntity(input);
		return result;
	}
	public async Task<string> Redirect(string? shortUrl)
	{
		string redirectUrl = "https://azure.com";
		try
		{
			redirectUrl = Environment.GetEnvironmentVariable("DefaultRedirectUrl") ?? redirectUrl;

			if (!string.IsNullOrWhiteSpace(shortUrl))
			{
				var tempUrl = new ShortUrlEntity(string.Empty, shortUrl);
				var newUrl = await _stgHelper.GetShortUrlEntity(tempUrl);

				if (newUrl != null)
				{
					_logger.LogInformation($"Found it: {newUrl.Url}");

					if (newUrl.IsArchived ?? false)
					{
						_logger.LogInformation($"This URL is archived.");
						return redirectUrl;
					}
					
					newUrl.Clicks++;
					await _stgHelper.SaveClickStatsEntity(new ClickStatsEntity(newUrl.RowKey));
					await _stgHelper.SaveShortUrlEntity(newUrl);
					redirectUrl = WebUtility.UrlDecode(newUrl.ActiveUrl);
				}
				else
				{
					_logger.LogInformation("Bad Link, resorting to fallback.");
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogInformation($"Problem accessing storage: {ex.Message}");
		}
		return redirectUrl;
	}

	public async Task<ListResponse> List(string host)
	{
		_logger.LogInformation($"Starting UrlList...");

		var result = new ListResponse();
		string userId = string.Empty;

		try
		{
			result.UrlList = await _stgHelper.GetAllShortUrlEntities();
			result.UrlList = result.UrlList.Where(p => !(p.IsArchived ?? false)).ToList();
			foreach (ShortUrlEntity url in result.UrlList)
			{
				url.ShortUrl = string.Equals(url.LinkType, LinkTypes.Mobile, StringComparison.OrdinalIgnoreCase)
					? string.Concat(host, "/m/", url.RowKey)
					: Utility.GetShortUrl(host, url.RowKey);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "An unexpected error was encountered.");
			throw;
		}

		return result;
	}

	public async Task<ShortResponse> Create(ShortRequest input, string host)
	{
		ShortResponse result;

		try
		{

			var linkType = string.IsNullOrWhiteSpace(input.LinkType) ? LinkTypes.Web : input.LinkType.Trim();
			if (!string.Equals(linkType, LinkTypes.Web, StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(linkType, LinkTypes.Mobile, StringComparison.OrdinalIgnoreCase))
			{
				throw new ShortenerToolException(HttpStatusCode.BadRequest, "The linkType parameter must be web or mobile.");
			}

			var isMobile = string.Equals(linkType, LinkTypes.Mobile, StringComparison.OrdinalIgnoreCase);
			if (isMobile && input.Schedules?.Length > 0)
			{
				throw new ShortenerToolException(HttpStatusCode.BadRequest, "Mobile links do not support schedules.");
			}

			if (!isMobile && string.IsNullOrWhiteSpace(input.Url))
			{
				throw new ShortenerToolException(HttpStatusCode.BadRequest, "The url parameter can not be empty.");
			}

			if (!isMobile && !Uri.IsWellFormedUriString(input.Url, UriKind.Absolute))
			{
				throw new ShortenerToolException(HttpStatusCode.BadRequest, $"{input.Url} is not a valid absolute Url. The Url parameter must start with 'http://' or 'http://'.");
			}

			string longUrl = isMobile ? GetMobilePortalUrl() : input.Url!.Trim();
			string vanity = string.IsNullOrWhiteSpace(input.Vanity) ? "" : input.Vanity.Trim();
			string title = string.IsNullOrWhiteSpace(input.Title) ? "" : input.Title.Trim();

			ShortUrlEntity newRow;

			if (!string.IsNullOrEmpty(vanity))
			{
				if (Utility.IsReservedVanity(vanity))
				{
					throw new ShortenerToolException(HttpStatusCode.BadRequest, "This Short URL is reserved.");
				}

				newRow = new ShortUrlEntity(longUrl, vanity, title, input.Schedules);

				if (await _stgHelper.IfShortUrlEntityExist(newRow))
				{
					throw new ShortenerToolException(HttpStatusCode.Conflict, "This Short URL already exist.");
				}
			}
			else
			{
				var generatedVanity = await Utility.GetValidEndUrl(vanity, _stgHelper);
				newRow = new ShortUrlEntity(longUrl, generatedVanity, title, input.Schedules);
			}

			newRow.LinkType = isMobile ? LinkTypes.Mobile : LinkTypes.Web;
			if (isMobile)
			{
				newRow.Data = input.Data ?? new Dictionary<string, string>();
			}

			await _stgHelper.SaveShortUrlEntity(newRow);

			result = new ShortResponse(host, newRow.Url, newRow.RowKey, newRow.Title);
			result.LinkType = newRow.LinkType;
			result.Data = newRow.Data;
			if (isMobile)
			{
				result.ShortUrl = string.Concat(host, "/m/", newRow.RowKey);
			}

			_logger.LogInformation("Short Url created.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, ex.Message);
			throw;
		}

		return result;
	}

	public async Task<ShortUrlEntity> Update(ShortUrlEntity input, string host, MobileUpdateFields fields)
	{
		ShortUrlEntity result;

		try
		{
			var original = await _stgHelper.GetShortUrlEntity(input);
			if (string.Equals(original.LinkType, LinkTypes.Mobile, StringComparison.OrdinalIgnoreCase))
			{
				if (fields.LinkTypeProvided && !string.Equals(input.LinkType, LinkTypes.Mobile, StringComparison.OrdinalIgnoreCase))
				{
					throw new ShortenerToolException(HttpStatusCode.BadRequest, "Mobile link type cannot be changed.");
				}

				if (fields.UrlProvided && !string.Equals(input.Url, original.Url, StringComparison.Ordinal))
				{
					throw new ShortenerToolException(HttpStatusCode.BadRequest, "Mobile link fallback cannot be changed.");
				}

				if (fields.SchedulesProvided && input.Schedules.Any())
				{
					throw new ShortenerToolException(HttpStatusCode.BadRequest, "Mobile links do not support schedules.");
				}

				if (fields.TitleProvided)
				{
					original.Title = input.Title;
				}

				if (fields.DataProvided)
				{
					original.Data = input.Data;
				}

				result = await _stgHelper.SaveShortUrlEntity(original);
				result.ShortUrl = string.Concat(host, "/m/", result.RowKey);
				return result;
			}

			// If the Url parameter only contains whitespaces or is empty return with BadRequest.
			if (string.IsNullOrWhiteSpace(input.Url))
			{
				throw new ShortenerToolException(HttpStatusCode.BadRequest, "The url parameter can not be empty.");
			}

			// Validates if input.url is a valid aboslute url, aka is a complete refrence to the resource, ex: http(s)://google.com
			if (!Uri.IsWellFormedUriString(input.Url, UriKind.Absolute))
			{
				throw new ShortenerToolException(HttpStatusCode.BadRequest, $"{input.Url} is not a valid absolute Url. The Url parameter must start with 'http://' or 'http://'.");
			}

			result = await _stgHelper.UpdateShortUrlEntity(input);
			result.ShortUrl = Utility.GetShortUrl(host, result.RowKey);

		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "An unexpected error was encountered.");
			throw;
		}

		return result;
	}

	private static string GetMobilePortalUrl()
	{
		return Environment.GetEnvironmentVariable("ChroniclePortalUrl") ?? DefaultMobilePortalUrl;
	}

	public async Task<ClickDateList> ClickStatsByDay(UrlClickStatsRequest input, string host)
	{
		var result = new ClickDateList();
		try
		{
			var rawStats = await _stgHelper.GetAllStatsByVanity(input.Vanity, input.StartDate, input.EndDate);

			result.Items = rawStats.GroupBy(s => DateTime.Parse(s.Datetime).Date)
										.Select(stat => new ClickDate
										{
											DateClicked = DateTime.Parse(stat.Key.ToString("yyyy-MM-dd")),
											Count = stat.Count()
										}).OrderBy(s => s.DateClicked).ToList<ClickDate>();

			result.Url = Utility.GetShortUrl(host, input.Vanity);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "An unexpected error was encountered.");
			throw;
		}
		return result;
	}

	public async Task<bool> ImportUrlDataAsync(UrlDetails urlData)
	{
		try
		{
			await _stgHelper.ImportUrlDataAsync(urlData);
			return true;
		}
		catch (Exception ex)
		{
            _logger.LogError(ex, "An unexpected error was encountered.");
            throw;
		}
	}

	public async Task<bool> ImportClickStatsAsync(List<ClickStatsEntity> lstClickStats)
	{
		try
		{
			await _stgHelper.ImportClickStatsAsync(lstClickStats);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "An unexpected error was encountered.");
			throw;
		}
	}
}

public record MobileUpdateFields(bool TitleProvided, bool DataProvided, bool UrlProvided, bool SchedulesProvided, bool LinkTypeProvided);
