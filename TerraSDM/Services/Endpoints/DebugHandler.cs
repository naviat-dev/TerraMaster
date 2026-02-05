namespace TerraSDM.Services.Endpoints;

internal class DebugHttpHandler(HttpMessageHandler? innerHandler = null) : DelegatingHandler(innerHandler ?? new HttpClientHandler())
{
	protected async override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
#if DEBUG
		if (!response.IsSuccessStatusCode)
		{
			Console.Error.WriteLine("Unsuccessful API Call");
			if (request.RequestUri is not null)
			{
				Console.Error.WriteLine($"{request.RequestUri} ({request.Method})");
			}

			foreach ((string key, string values) in request.Headers.ToDictionary(x => x.Key, x => string.Join(", ", x.Value)))
			{
				Console.Error.WriteLine($"  {key}: {values}");
			}

			string? content = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
			if (!string.IsNullOrEmpty(content))
			{
				Console.Error.WriteLine(content);
			}

			// Uncomment to automatically break when an API call fails while debugging
			// System.Diagnostics.Debugger.Break();
		}
#endif
		return response;
	}
}
