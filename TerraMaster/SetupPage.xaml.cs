using Microsoft.UI.Xaml.Media.Animation;

namespace TerraMaster;

public sealed partial class SetupPage : Page
{
	public SetupPage()
	{
		InitializeComponent();
		Loaded += (sender, e) =>
		{

		};
	}

	private void NextPage(object sender, RoutedEventArgs e)
	{
		Storyboard storyboard = Ui.SlideOutAnimation("X", TimeSpan.FromSeconds(0.5), MainGrid, MainTransform);
		storyboard.Completed += static (s, args) =>
		{
			_ = ((Frame)Window.Current.Content).Navigate(typeof(LoadingPage));
		};
		storyboard.Begin();
	}
}
