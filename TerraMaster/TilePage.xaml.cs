namespace TerraMaster;

public sealed partial class TilePage : Page
{
	public TilePage()
	{
		InitializeComponent();
		_ = Cesium.StartAsync();
		_ = MyWebView.EnsureCoreWebView2Async();
		MyWebView.Source = new Uri(Cesium.Url + "/assets/cesium.js.html");
		MyWebView.WebMessageReceived += (sender, e) =>
			{
				string message = e.TryGetWebMessageAsString();
				if (message.StartsWith("LOG:"))
					Console.WriteLine("[JS LOG] " + message[4..]);
				else if (message.StartsWith("ERR:"))
					Console.WriteLine("[JS ERR] " + message[4..]);
			};
	}
}
