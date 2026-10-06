using Azure.Data.Tables;
using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Messages;
using Cloud5mins.ShortenerTools.Core.Service;
using Cloud5mins.ShortenerTools.Core.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Net;
using System.Text.Json;

public static class ShortenerEnpoints
{
    public static void MapShortenerEnpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("api")
                .WithOpenApi()
                .AddEndpointFilter(async (context, next) =>
                {
                    var httpContext = context.HttpContext;
                    // You can change the header name if needed
                    if (!httpContext.Request.Headers.TryGetValue("x-api-key", out var extractedApiKey))
                    {
                        return Results.Unauthorized();
                    }

                    // Ideally, get the API key from configuration/environment
                    var apiKey = Environment.GetEnvironmentVariable("APIKey");
                    if (!string.Equals(extractedApiKey, apiKey, StringComparison.Ordinal))
                    {
                        return Results.Unauthorized();
                    }

                    return await next(context);
                });

        // GETS

        endpoints.MapGet("/", GetWelcomeMessage)
            .WithDescription("Welcome to Cloud5mins URL Shortener API");

        endpoints.MapGet("/UrlList", UrlList)
            .WithDescription("List all Urls")
            .WithDisplayName("Url List");


        // POSTS

        endpoints.MapPost("/UrlCreate", UrlCreate)
            .WithDescription("Create a new Short URL")
            .WithDisplayName("Url Create");

        endpoints.MapPost("/UrlUpdate", UrlUpdate)
            .WithDescription("Update a Url")
            .WithDisplayName("Url Update");

        endpoints.MapPost("/UrlArchive", UrlArchive)
            .WithDescription("Archive a Url")
            .WithDisplayName("Url Archive");

        endpoints.MapPost("/UrlClickStatsByDay", UrlClickStatsByDay)
            .WithDescription("Provide Click Statistics by Day")
            .WithDisplayName("Url Click Statistics By Day");

        endpoints.MapPost("/UrlDataImport", UrlDataImport)
            .WithDescription("Import Urls from a CSV file")
            .WithDisplayName("Url Data Import");

        endpoints.MapPost("/UrlClickStatsImport", UrlClickStatsImport)
            .WithDescription("Import Click Statistics from a CSV file")
            .WithDisplayName("Url Click Statistics Import");

    }

    static private string GetWelcomeMessage()
    {
        return "Welcome to Cloud5mins URL Shortener API";
    }

    static private async Task<Results<
                                Created<ShortResponse>,
                                BadRequest<DetailedBadRequest>,
                                NotFound<DetailedBadRequest>,
                                Conflict<DetailedBadRequest>,
                                InternalServerError<DetailedBadRequest>
                                >> UrlCreate(JsonDocument requestBody,
                                                 TableServiceClient tblClient,
                                                HttpContext context,
                                                ILogger logger)
    {
        try
        {
            if (!TryDeserializeRequest(requestBody, out ShortRequest? request, out var error))
            {
                return TypedResults.BadRequest(new DetailedBadRequest { Message = error });
            }

            var urlServices = new UrlServices(logger, new AzStrorageTablesService(tblClient));
            var host = GetHost(context);
            ShortResponse result = await urlServices.Create(request!, host);
            return TypedResults.Created($"/api/UrlCreate/{result.ShortUrl}", result);
        }
        catch (ShortenerToolException ex)
        {
            switch (ex.StatusCode)
            {
                case HttpStatusCode.BadRequest:
                    return TypedResults.BadRequest<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
                case HttpStatusCode.NotFound:
                    return TypedResults.NotFound<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
                case HttpStatusCode.Conflict:
                    return TypedResults.Conflict<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
                default:
                    return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
            }
        }
        catch (JsonException ex)
        {
            return TypedResults.BadRequest(new DetailedBadRequest { Message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error was encountered.");
            return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
        }
    }

    static private async Task<Results<
                                    Ok,
                                    InternalServerError<DetailedBadRequest>>>
                                    UrlArchive(ShortUrlEntity shortUrl,
                                                TableServiceClient tblClient,
                                                ILogger logger)
    {
        try
        {
            var urlServices = new UrlServices(logger, new AzStrorageTablesService(tblClient));
            var result = await urlServices.Archive(shortUrl);
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
        }
    }

    static private async Task<Results<
                                    Ok<ShortUrlEntity>,
                                    BadRequest<DetailedBadRequest>,
                                    InternalServerError<DetailedBadRequest>>>
                                    UrlUpdate(JsonDocument requestBody,
                                                TableServiceClient tblClient,
                                                HttpContext context,
                                                ILogger logger)
    {
        try
        {
            if (!IsValidData(requestBody.RootElement, out var error))
            {
                return TypedResults.BadRequest(new DetailedBadRequest { Message = error });
            }

            var shortUrl = requestBody.Deserialize<ShortUrlEntity>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (shortUrl == null)
            {
                return TypedResults.BadRequest(new DetailedBadRequest { Message = "The request body is required." });
            }

            var urlServices = new UrlServices(logger, new AzStrorageTablesService(tblClient));
            var host = GetHost(context);
            var result = await urlServices.Update(shortUrl, host, new MobileUpdateFields(
                HasProperty(requestBody.RootElement, "title"),
                HasProperty(requestBody.RootElement, "data"),
                HasProperty(requestBody.RootElement, "url"),
                HasProperty(requestBody.RootElement, "schedules") || HasProperty(requestBody.RootElement, "schedulesPropertyRaw"),
                HasProperty(requestBody.RootElement, "linkType")));
            return TypedResults.Ok(result);
        }
        catch (ShortenerToolException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            return TypedResults.BadRequest(new DetailedBadRequest { Message = ex.Message });
        }
        catch (JsonException ex)
        {
            return TypedResults.BadRequest(new DetailedBadRequest { Message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
        }
    }

    private static bool HasProperty(JsonElement body, string name)
    {
        return body.EnumerateObject().Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryDeserializeRequest(JsonDocument requestBody, out ShortRequest? request, out string error)
    {
        request = null;
        if (!IsValidData(requestBody.RootElement, out error))
        {
            return false;
        }

        try
        {
            request = requestBody.Deserialize<ShortRequest>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (request == null)
            {
                error = "The request body is required.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsValidData(JsonElement body, out string error)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            error = "The request body must be a JSON object.";
            return false;
        }

        JsonElement data = default;
        var dataFound = false;
        foreach (var property in body.EnumerateObject())
        {
            if (!string.Equals(property.Name, "data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (dataFound)
            {
                error = "The data parameter must not be specified more than once.";
                return false;
            }

            data = property.Value;
            dataFound = true;
        }

        if (!dataFound)
        {
            error = string.Empty;
            return true;
        }

        if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Any(property => property.Value.ValueKind != JsonValueKind.String))
        {
            error = "The data parameter must be an object with string values.";
            return false;
        }

        error = string.Empty;
        return true;
    }



    static private async Task<Results<
                                    Ok<ClickDateList>,
                                    InternalServerError<DetailedBadRequest>>>
                                    UrlClickStatsByDay(UrlClickStatsRequest statsRequest,
                                                TableServiceClient tblClient,
                                                HttpContext context,
                                                ILogger logger)
    {
        try
        {
            var urlServices = new UrlServices(logger, new AzStrorageTablesService(tblClient));
            var host = GetHost(context);
            var result = await urlServices.ClickStatsByDay(statsRequest, host);
            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
        }
    }


    static private async Task<Results<
                                Ok<ListResponse>,
                                InternalServerError<DetailedBadRequest>>>
                                UrlList(TableServiceClient tblClient,
                                        HttpContext context,
                                        ILogger logger)
    {
        try
        {
            var urlServices = new UrlServices(logger, new AzStrorageTablesService(tblClient));
            var host = GetHost(context);
            ListResponse Urls = await urlServices.List(host);
            return TypedResults.Ok(Urls);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error was encountered.");
            return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
        }
    }

    private static string GetHost(HttpContext context)
    {
        string? customDomain = Environment.GetEnvironmentVariable("CustomDomain");
        var host = string.IsNullOrEmpty(customDomain) ? context.Request.Host.Value : customDomain;
        return host ?? string.Empty;
    }


    static private async Task<Results<
									Ok,
									InternalServerError<DetailedBadRequest>>>
									UrlDataImport(UrlDetails data,
													TableServiceClient tblClient,
													ILogger logger)
	{
		try
		{
			var urlServices = new UrlServices(logger, new AzStrorageTablesService(tblClient));
			await urlServices.ImportUrlDataAsync(data);
			return TypedResults.Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex.Message);
			return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
		}
	}

	static private async Task<Results<
									Ok,
									InternalServerError<DetailedBadRequest>>>
									UrlClickStatsImport(List<ClickStatsEntity> lstClickStats,
												TableServiceClient tblClient,
												ILogger logger)
	{
		try
		{
			var urlServices = new UrlServices(logger, new AzStrorageTablesService(tblClient));
			await urlServices.ImportClickStatsAsync(lstClickStats);
			return TypedResults.Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex.Message);
			return TypedResults.InternalServerError<DetailedBadRequest>(new DetailedBadRequest { Message = ex.Message });
		}
	}

}
