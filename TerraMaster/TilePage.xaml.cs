namespace TerraMaster;

public sealed partial class TilePage : Page
{
    public TilePage()
    {
        this.InitializeComponent();
        _ = Cesium.StartAsync();
        _ = MyWebView.EnsureCoreWebView2Async();
        MyWebView.Source = new Uri(Cesium.Url + "/assets/cesium.js.html");
    }
}
