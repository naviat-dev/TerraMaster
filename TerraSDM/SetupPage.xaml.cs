using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;

namespace TerraSDM;

public sealed partial class SetupPage : Page
{
	private static readonly string[] TitleText = [
		"Welcome to TerraSDM",
		"Where do you want to save your scenery?",
		"Configure Cesium API",
		"You're all set!"
	];
	private static readonly string[] BodyText = [
		"Just a few more steps to get you started.",
		"If you have scenery already downloaded, you can point TerraSDM to that location.",
		"Enter your Cesium Ion API access token to download terrain and imagery.",
		"TerraSDM is ready to help you manage your scenery data."
	];
#pragma warning disable IDE0044 // Add readonly modifier
	private int _currentPage = 0;
	private StorageFolder? _selectedFolder;
#pragma warning restore IDE0044 // Add readonly modifier
	public SetupPage()
	{
		InitializeComponent();
		Loaded += (sender, e) =>
		{
			UpdatePageVisibility();
		};
	}

	private async void SelectFolder(object sender, RoutedEventArgs e)
	{
		FolderPicker folderPicker = new()
		{
			SuggestedStartLocation = PickerLocationId.Desktop
		};
		folderPicker.FileTypeFilter.Add("*"); // Required for folder picker

#if WINDOWS
        // Associate with current window for WinUI 3
        var window = ((App)Application.Current).MainWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
#endif

		_selectedFolder = await folderPicker.PickSingleFolderAsync();

		if (_selectedFolder != null)
		{
			// Update UI to show selected path
			selectedPathText.Text = _selectedFolder.Path;
			startSetupButton.IsEnabled = true;
		}
	}

	private void NextPage(object sender, RoutedEventArgs e)
	{
		// Validate current page before proceeding
		if (_currentPage == 1 && _selectedFolder == null)
		{
			// Show error or return
			return;
		}
		if (_currentPage == 2 && string.IsNullOrWhiteSpace(cesiumTokenInput.Text))
		{
			// Show error or allow skip
			// For now, we'll allow continuing
		}

		_currentPage++;
		if (_currentPage >= TitleText.Length)
		{
			// Save configuration before proceeding
			SaveConfiguration();

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
				UpdatePageVisibility();
				Storyboard slideIn = Ui.SlideInAnimation("X", TimeSpan.FromSeconds(0.5), MainGrid, MainTransform);
				slideIn.Begin();
			};
			slideOut.Begin();
		}
	}

	private void UpdatePageVisibility()
	{
		// Show/hide page-specific content
		folderSelectionPanel.Visibility = _currentPage == 1 ? Visibility.Visible : Visibility.Collapsed;
		apiTokenPanel.Visibility = _currentPage == 2 ? Visibility.Visible : Visibility.Collapsed;
		summaryPanel.Visibility = _currentPage == 3 ? Visibility.Visible : Visibility.Collapsed;

		// Update button text
		if (_currentPage == TitleText.Length - 1)
		{
			startSetupButton.Content = "Get Started";
		}
		else
		{
			startSetupButton.Content = "->";
		}

		// Update summary if on last page
		if (_currentPage == 3)
		{
			summarySceneryPath.Text = _selectedFolder?.Path ?? "Not selected";
			summaryApiToken.Text = string.IsNullOrWhiteSpace(cesiumTokenInput.Text) ? "Not configured" : "Configured";
		}
	}

	private void SaveConfiguration()
	{
		// Save to Config or application settings
		if (_selectedFolder != null)
		{
			// Config.DataPath = _selectedFolder.Path;
		}
		if (!string.IsNullOrWhiteSpace(cesiumTokenInput.Text))
		{
			// Config.CesiumApiToken = cesiumTokenInput.Text;
		}
	}
}
