using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace TerraMaster;

public static class Cesium
{
	private static IHost? _host;
	public static string Url { get; private set; } = "http://localhost:5005";

	public static async Task StartAsync()
	{
		LoadingPage.RaiseLoadingChanged("Starting Cesium server...");
		WebApplicationBuilder builder = WebApplication.CreateBuilder();
		_ = builder.WebHost.UseUrls(Url);

		WebApplication app = builder.Build();

		_ = app.UseStaticFiles(new StaticFileOptions
		{
			FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "Assets")),
			OnPrepareResponse = ctx =>
			{
				ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
				ctx.Context.Response.Headers.Pragma = "no-cache";
				ctx.Context.Response.Headers.Expires = "0";
			}
		});

		// API endpoints for JavaScript to call C# functions

		_ = app.MapGet("/api/tileindex/{lat:double}/{lon:double}", (double lat, double lon) =>
		{
			int tileIndex = Util.GetTileIndex(lat, lon);
			return Results.Json(new { tileIndex = tileIndex });
		});

		// Optionally, serve cesium.js.html at root
		_ = app.MapGet("/", async context =>
		{
			context.Response.ContentType = "text/html";
			context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
			context.Response.Headers.Pragma = "no-cache";
			context.Response.Headers.Expires = "0";

			string htmlPath = Path.Combine(app.Environment.ContentRootPath, "Assets", "cesium.js.html");
			if (File.Exists(htmlPath))
			{
				await context.Response.SendFileAsync(htmlPath, 0, null, context.RequestAborted);
			}
			else
			{
				context.Response.StatusCode = StatusCodes.Status404NotFound;
			}
		});

		_host = app;
		_ = app.RunAsync();
	}

	public static async Task StopAsync()
	{
		if (_host != null)
		{
			await _host.StopAsync();
			_host.Dispose();
			_host = null;
		}
	}
}
