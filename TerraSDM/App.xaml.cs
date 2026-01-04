namespace TerraSDM;

public partial class App : Application
{
	/// <summary>
	/// Initializes the singleton application object. This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		InitializeComponent();

		// Initialize logging system
		Logger.Initialize();
		Logger.Info("App", "TerraSDM application starting");

		Suspending += static (s, e) =>
		{
			Logger.Info("App", "Application suspending");
			Cesium.StopAsync().Wait(TimeSpan.FromSeconds(3));
		};
	}

	protected Window? MainWindow { get; private set; }

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		Logger.Debug("App", "OnLaunched called");

		MainWindow = new Window();
#if DEBUG
		// MainWindow.UseStudio(); // Removed: 'Window' does not contain a definition for 'UseStudio'
#endif


		// Do not repeat app initialization when the Window already has content,
		// just ensure that the window is active
		if (MainWindow.Content is not Frame rootFrame)
		{
			// Create a Frame to act as the navigation context and navigate to the first page
			rootFrame = new Frame();

			// Place the frame in the current Window
			MainWindow.Content = rootFrame;

			rootFrame.NavigationFailed += OnNavigationFailed;
		}

		if (rootFrame.Content == null)
		{
			// When the navigation stack isn't restored navigate to the first page,
			// configuring the new page by passing required information as a navigation
			// parameter
			if (File.Exists(Util.ConfigPath) && Config.Load())
			{
				// Config loaded successfully, go to main page
				Logger.Info("App", "Configuration loaded, navigating to LoadingPage");
				_ = rootFrame.Navigate(typeof(LoadingPage), args.Arguments);
			}
			else
			{
				// No config or failed to load, show setup page
				Logger.Info("App", "No configuration found, navigating to SetupPage");
				_ = rootFrame.Navigate(typeof(SetupPage), args.Arguments);
			}
		}

		// Ensure the current window is active
		MainWindow.Activate();
	}

	/// <summary>
	/// Invoked when Navigation to a certain page fails
	/// </summary>
	/// <param name="sender">The Frame which failed navigation</param>
	/// <param name="e">Details about the navigation failure</param>
	private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
	{
		Logger.Error("App", $"Failed to load {e.SourcePageType.FullName}", e.Exception);
		throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
	}

	/// <summary>
	/// Configures global Uno Platform logging
	/// </summary>
	public static void InitializeLogging()
	{
#if DEBUG
		// Logging is disabled by default for release builds, as it incurs a significant
		// initialization cost from Microsoft.Extensions.Logging setup. If startup performance
		// is a concern for your application, keep this disabled. If you're running on the web or
		// desktop targets, you can use URL or command line parameters to enable it.
		//
		// For more performance documentation: https://platform.uno/docs/articles/Uno-UI-Performance.html

		var factory = LoggerFactory.Create(builder =>
		{
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());

            // Log to the Visual Studio Debug console
            builder.AddConsole();
#else
			builder.AddConsole();
#endif

			// Exclude logs below this level
			builder.SetMinimumLevel(LogLevel.Information);

			// Default filters for Uno Platform namespaces
			builder.AddFilter("Uno", LogLevel.Warning);
			builder.AddFilter("Windows", LogLevel.Warning);
			builder.AddFilter("Microsoft", LogLevel.Warning);
		});

		Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
		Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
	}

	/// <summary>
	/// Initializes the TerraSDM application by setting up necessary directories, downloading and parsing the airport index,
	/// reading scenery metadata, and initiating downloads for specific tiles and flight plans.
	/// </summary>
	public async static Task TMStart()
	{
		if (!Directory.Exists(Util.TempPath))
		{
			_ = Directory.CreateDirectory(Util.TempPath);
			LoadingPage.RaiseLoadingChanged("Creating temp directory...");
		}
		DownloadMgr.client.Timeout = new TimeSpan(0, 10, 0);
		DownloadMgr.DownloadPlan("C:\\Users\\sriem\\Downloads\\test2.fgfp", 100);
	}
}
