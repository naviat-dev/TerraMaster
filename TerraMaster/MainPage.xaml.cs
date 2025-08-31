namespace TerraMaster;

public sealed partial class MainPage : Page
{
	public static event Action<string> LoadingChanged;
	public static string ErrorMessage;

	public MainPage()
	{

		InitializeComponent();
		LoadingChanged += (message) => loadingText.Text = message;
		this.Loaded += (s, e) =>
		{
			try
			{
				Startup();
			}
			catch (Exception)
            {
				refreshButton.SetValue(VisibilityProperty, Visibility.Visible);
			}
		};
	}

	private async void Startup()
	{
		refreshButton.SetValue(VisibilityProperty, Visibility.Collapsed);
		await App.TMStart();
		await Cesium.StartAsync();
		_ = Frame.Navigate(typeof(TilePage));
	}

	private void Refresh(object sender, RoutedEventArgs e)
	{
		Startup();
	}

	public static void RaiseLoadingChanged(string message)
		=> LoadingChanged?.Invoke(message);
}
