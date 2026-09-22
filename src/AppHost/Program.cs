
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var customDomain = builder.AddParameter("CustomDomain");
var defaultRedirectUrl = builder.AddParameter("DefaultRedirectUrl");
var apiKey = builder.AddParameter("APIKey");
var chroniclePortalUrl = builder.AddParameter("ChroniclePortalUrl");
var chronicleUriScheme = builder.AddParameter("ChronicleUriScheme");
var chronicleIosAppId = builder.AddParameter("ChronicleIosAppId");
var chronicleAndroidPackage = builder.AddParameter("ChronicleAndroidPackage");
var chronicleAndroidSigningFingerprints = builder.AddParameter("ChronicleAndroidSigningFingerprints");

var urlStorage = builder.AddAzureStorage("url-data");

if (builder.Environment.IsDevelopment())
{
    urlStorage.RunAsEmulator();
}

var strTables = urlStorage.AddTables("strTables");

var azFuncLight = builder.AddAzureFunctionsProject<Projects.Cloud5mins_ShortenerTools_FunctionsLight>("azfunc-light")
							.WithReference(strTables)
							.WaitFor(strTables)
							.WithEnvironment("DefaultRedirectUrl",defaultRedirectUrl)
							.WithEnvironment("ChroniclePortalUrl", chroniclePortalUrl)
							.WithEnvironment("ChronicleUriScheme", chronicleUriScheme)
							.WithEnvironment("ChronicleIosAppId", chronicleIosAppId)
							.WithEnvironment("ChronicleAndroidPackage", chronicleAndroidPackage)
							.WithEnvironment("ChronicleAndroidSigningFingerprints", chronicleAndroidSigningFingerprints)
							.WithExternalHttpEndpoints();

var manAPI = builder.AddProject<Projects.Cloud5mins_ShortenerTools_Api>("api")
						.WithReference(strTables)
						.WaitFor(strTables)
						.WithEnvironment("CustomDomain",customDomain)
						.WithEnvironment("DefaultRedirectUrl",defaultRedirectUrl)
						.WithEnvironment("ChroniclePortalUrl", chroniclePortalUrl)
						.WithEnvironment("APIKey", apiKey)
						.WithExternalHttpEndpoints(); // If you want to access the API directly

builder.AddProject<Projects.Cloud5mins_ShortenerTools_TinyBlazorAdmin>("admin")
		.WithExternalHttpEndpoints()
		.WithReference(manAPI);

builder.Build().Run();
