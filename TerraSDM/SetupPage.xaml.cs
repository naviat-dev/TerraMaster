using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;

namespace TerraSDM;

public sealed partial class SetupPage : Page
{
	private static readonly string[] TitleText = ["Welcome to TerraSDM", "Where do you want to save your scenery?"];
	private static readonly string[] BodyText = ["Just a few more steps to get you started.", "If you have scenery already downloaded, you can point TerraSDM to that location."];
#pragma warning disable IDE0044 // Add readonly modifier
	private int _currentPage = 0;
#pragma warning restore IDE0044 // Add readonly modifier
	public SetupPage()
	{
		InitializeComponent();
		Loaded += (sender, e) =>
		{

		};
	}

	private async void SelectFolder(object sender, RoutedEventArgs e)
	{
		var folderPicker = new FolderPicker();
		folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
		folderPicker.FileTypeFilter.Add("*"); // Required for folder picker

#if WINDOWS
        // Associate with current window for WinUI 3
        var window = ((App)Application.Current).MainWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
#endif

		StorageFolder _selectedFolder = await folderPicker.PickSingleFolderAsync();

		/* if (_selectedFolder != null)
		{
			// Update UI to show selected path
			selectedPathText.Text = _selectedFolder.Path;
			startSetupButton.IsEnabled = true;

			// Optionally save to config or application settings
			// Config.DataPath = _selectedFolder.Path;
		} */
	}

	private void NextPage(object sender, RoutedEventArgs e)
	{
		_currentPage++;
		if (_currentPage >= TitleText.Length)
		{
			Storyboard slideOut = Ui.SlideOutAnimation("X", TimeSpan.FromSeconds(0.5), MainGrid, MainTransform);
			slideOut.Completed += (s, args) =>
			{
				_ = ((Frame)Window.Current.Content).Navigate(typeof(LoadingPage));
			};
			slideOut.Begin();
		}
		else
		{
			Storyboard slideOut = Ui.SlideOutAnimation("X", TimeSpan.FromSeconds(0.5), MainGrid, MainTransform);
			slideOut.Completed += (s, args) =>
			{
				setupTitle.Text = TitleText[_currentPage];
				setupBody.Text = BodyText[_currentPage];
				if (_currentPage == 1)
				{

				}
				Storyboard slideIn = Ui.SlideInAnimation("X", TimeSpan.FromSeconds(0.5), MainGrid, MainTransform);
				slideIn.Begin();
			};
			slideOut.Begin();
		}
	}
}
