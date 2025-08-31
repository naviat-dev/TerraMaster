namespace TerraMaster;

public sealed partial class TilePage : Page
{
	public TilePage()
	{
		InitializeComponent();
		_ = MyWebView.EnsureCoreWebView2Async();
		MyWebView.Source = new Uri(Cesium.Url + "/assets/cesium.js.html");
		MyWebView.WebMessageReceived += (s, e) =>
		{
			var message = e.TryGetWebMessageAsString();
			Console.WriteLine("[JS: " + message + "]");
		};
	}
}
