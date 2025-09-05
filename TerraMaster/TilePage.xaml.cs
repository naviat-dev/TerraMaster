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
			var message = e.WebMessageAsJson;
			Console.WriteLine("[JS: " + message + "]");
		};
	}
}
