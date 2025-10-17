namespace TerraMaster;

public sealed partial class LoadingPage : Page
{
	public static event Action<string>? LoadingChanged;
	public static string? ErrorMessage { get; private set; }

	public LoadingPage()
	{

		InitializeComponent();
		LoadingChanged += (message) =>
		{
			// Ensure UI updates are marshaled to the UI thread
			if (Dispatcher.HasThreadAccess)
			{
				loadingText.Text = message;
			}
			else
			{
				_ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
				{
					loadingText.Text = message;
				});
			}
		};
		Loaded += async (s, e) =>
		{
			try
			{
				await Startup();
			}
			catch (Exception ex)
			{
				refreshButton.Visibility = Visibility.Visible;
			}
		};
	}

	private async Task Startup()
	{
		refreshButton.Visibility = Visibility.Collapsed;
		await App.TMStart();
		await Cesium.StartAsync();
		_ = Frame.Navigate(typeof(TilePage));
	}

	private async void Refresh(object sender, RoutedEventArgs e)
	{
		await Startup();
	}

	public static void RaiseLoadingChanged(string message)
		=> LoadingChanged?.Invoke(message);
}
