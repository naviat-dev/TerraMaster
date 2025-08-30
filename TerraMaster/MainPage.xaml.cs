namespace TerraMaster;

public sealed partial class MainPage : Page
{
	public MainPage()
	{
		InitializeComponent();
		this.Loaded += (s, e) =>
		{
			App.TMStart();
			// _ = Frame.Navigate(typeof(TilePage));
		};
	}
}
