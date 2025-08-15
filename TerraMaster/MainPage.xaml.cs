namespace TerraMaster;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        _ = ((Frame)Window.Current.Content).Navigate(typeof(TilePage));
    }

    private void GoToTilePage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Tile page
        _ = Frame.Navigate(typeof(TilePage));
    }

    private void GoToArptPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Airport page
        _ = Frame.Navigate(typeof(AirportPage));
    }

    private void GoToPlanPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Plan page
        _ = Frame.Navigate(typeof(PlanPage));
    }

    private void GoToFGPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the FG page
        _ = Frame.Navigate(typeof(FGPage));
    }

    private void GoToSettingsPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Settings page
        _ = Frame.Navigate(typeof(SettingsPage));
    }

    private void GoToInfoPage(object sender, RoutedEventArgs e)
    {
        // Navigate to the Info page
        _ = Frame.Navigate(typeof(InfoPage));
    }
}
