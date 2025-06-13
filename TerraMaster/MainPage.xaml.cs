namespace TerraMaster;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
    }

    private void GoToTilePage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Tile page
        Frame.Navigate(typeof(TilePage));
    }

    private void GoToArptPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Airport page
        Frame.Navigate(typeof(AirportPage));
    }

    private void GoToPlanPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Plan page
        Frame.Navigate(typeof(PlanPage));
    }

    private void GoToFGPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the FG page
        Frame.Navigate(typeof(FGPage));
    }

    private void GoToSettingsPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Settings page
        Frame.Navigate(typeof(SettingsPage));
    }

    private void GoToInfoPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Info page
        Frame.Navigate(typeof(InfoPage));
    }
}
