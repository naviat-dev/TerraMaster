namespace TerraMaster;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
    }

    private void GoToSettingsPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Settings page
        Frame.Navigate(typeof(SettingsPage));
    }
}
