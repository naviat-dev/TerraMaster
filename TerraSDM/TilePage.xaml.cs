namespace TerraSDM;

public sealed partial class TilePage : Page
{
	public TilePage()
	{
		InitializeComponent();
		_ = MyWebView.EnsureCoreWebView2Async();
		MyWebView.Source = new Uri("http://localhost:5005/");
		MyWebView.WebMessageReceived += (s, e) =>
		{
			string message = e.WebMessageAsJson;
			Logger.Debug("TilePage", $"[JS: {message}]");
		};
	}

	private void WebViewHost_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		MyWebView.Width = e.NewSize.Width;
		MyWebView.Height = e.NewSize.Height;
	}
}
