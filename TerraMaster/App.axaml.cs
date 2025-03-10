using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TerraMaster;

public partial class App : Application
{
    double[,] LAT_INDEX = {{89, 12}, {86, 4}, {83, 2}, {76, 1}, {62,  0.5}, {22,  0.25}, {0, 0.125}};
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public int GetTileIndex(double lat, double lon) {
        if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) {
            Console.WriteLine("Latitude or longitude out of range");
            return 0;
        } else {
            double lookup = Math.Abs(lat);
            double tileWidth = 0;
            for (int i = 0; i < LAT_INDEX.Length; i++) {
                if (lookup >= LAT_INDEX[i, 0]) {
                    tileWidth = LAT_INDEX[i, 1];
                    break;
                }
            }
            int baseX = (int) Math.Floor(Math.Floor(lon / tileWidth) * tileWidth);
            int x = (int) Math.Floor((lon - baseX) / tileWidth);
            int baseY = (int) Math.Floor(lat);
            int y = (int) Math.Truncate((lat - baseY) * 8);
            return ((baseX + 180) << 14) + ((baseY + 90) << 6) + (y << 3) + x;
        };
    }
}