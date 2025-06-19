using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace TerraMaster;

public static class Cesium
{
    private static IHost _host;
    public static string Url { get; private set; } = "http://localhost:5005";

    public static Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();

        var app = builder.Build();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "Assets")),
            RequestPath = "/assets"
        });
        builder.WebHost.UseUrls("http://localhost:5000");


        return app.RunAsync("http://localhost:5000");
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
