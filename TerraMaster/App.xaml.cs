using Uno.Resizetizer;
using SharpCompress.Readers;
using SkiaSharp;
using System.Diagnostics;
using HtmlAgilityPack;
using System.Xml;

namespace TerraMaster;

public partial class App : Application
{
	static readonly double[,] LatitudeIndex = { { 89, 12 }, { 86, 4 }, { 83, 2 }, { 76, 1 }, { 62, 0.5 }, { 22, 0.25 }, { 0, 0.125 } };
	static string SavePath = "E:/testing/";
	static string TempPath;
	static readonly string TerrServerUrl = "https://terramaster.flightgear.org/terrasync/";
	static readonly string[] Ws2ServerUrls = ["https://terramaster.flightgear.org/terrasync/ws2/", "https://flightgear.sourceforge.net/scenery/"];

	static readonly Dictionary<string, double[]> Airports = [];
	static int OrthoRes = 2048;
	static bool InternetConnected = false;
	static HashSet<string> CurrentTasks = [];
	static SemaphoreSlim taskQueue = new(20);
	static Dictionary<string, string[]> TerrainRecords = [];
	static Dictionary<string, string[]> OrthoRecords = [];
	/// <summary>
	/// Initializes the singleton application object. This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
	}

	protected Window? MainWindow { get; private set; }

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		MainWindow = new Window();
#if DEBUG
		MainWindow.UseStudio();
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
			rootFrame.Navigate(typeof(MainPage), args.Arguments);
		}

		MainWindow.SetWindowIcon();
		// Ensure the current window is active
		MainWindow.Activate();

		TMStart();		
	}

	/// <summary>
	/// Invoked when Navigation to a certain page fails
	/// </summary>
	/// <param name="sender">The Frame which failed navigation</param>
	/// <param name="e">Details about the navigation failure</param>
	void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
	{
		throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
	}

	static void TMStart()
	{
		// Request airport index and parse into dictionary
		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };

		using (HttpClient client = new(handler))
		{
			Console.WriteLine("Downloading airport index...");
			string[] airports = System.Text.Encoding.UTF8.GetString(client.GetByteArrayAsync("https://terramaster.flightgear.org/terrasync/ws2/Airports/index.txt").Result).Split(["\r\n", "\n"], StringSplitOptions.None);
			foreach (string airport in airports)
			{
				if (airport == "") continue;
				string[] airportInfo = airport.Split("|");
				Airports.Add(airportInfo[0], [double.Parse(airportInfo[1]), double.Parse(airportInfo[2])]);
			}
		}
		Console.WriteLine("Done.");
		Console.WriteLine("Reading scenery metadata...");
		if (File.Exists(SavePath))
		{
		}

		_ = DownloadPlan("C:\\Users\\King\\Downloads\\YPDN-YSSY.fgfp", 30);
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

			// Generic Xaml events
			// builder.AddFilter("Microsoft.UI.Xaml", LogLevel.Debug );
			// builder.AddFilter("Microsoft.UI.Xaml.VisualStateGroup", LogLevel.Debug );
			// builder.AddFilter("Microsoft.UI.Xaml.StateTriggerBase", LogLevel.Debug );
			// builder.AddFilter("Microsoft.UI.Xaml.UIElement", LogLevel.Debug );
			// builder.AddFilter("Microsoft.UI.Xaml.FrameworkElement", LogLevel.Trace );

			// Layouter specific messages
			// builder.AddFilter("Microsoft.UI.Xaml.Controls", LogLevel.Debug );
			// builder.AddFilter("Microsoft.UI.Xaml.Controls.Layouter", LogLevel.Debug );
			// builder.AddFilter("Microsoft.UI.Xaml.Controls.Panel", LogLevel.Debug );

			// builder.AddFilter("Windows.Storage", LogLevel.Debug );

			// Binding related messages
			// builder.AddFilter("Microsoft.UI.Xaml.Data", LogLevel.Debug );
			// builder.AddFilter("Microsoft.UI.Xaml.Data", LogLevel.Debug );

			// Binder memory references tracking
			// builder.AddFilter("Uno.UI.DataBinding.BinderReferenceHolder", LogLevel.Debug );

			// DevServer and HotReload related
			// builder.AddFilter("Uno.UI.RemoteControl", LogLevel.Information);

			// Debug JS interop
			// builder.AddFilter("Uno.Foundation.WebAssemblyRuntime", LogLevel.Debug );
		});

		global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
		global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
	}

	/// <summary>
	/// Gets the index of a terrasync tile containing the given coordinates
	/// </summary>
	/// <param name="lat">The latitude of the point</param>
	/// <param name="lon">The longitude of the point</param>
	/// <returns>The index of the tile containing the point. If the point is outside the range of valid coordinates, returns 0.</returns>
	public static int GetTileIndex(double lat, double lon)
	{
		if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
		{
			Console.WriteLine("Latitude or longitude out of range");
			return 0;
		}
		else
		{
			double lookup = Math.Abs(lat);
			double tileWidth = 0;
			for (int i = 0; i < LatitudeIndex.Length; i++)
			{
				if (lookup >= LatitudeIndex[i, 0])
				{
					tileWidth = LatitudeIndex[i, 1];
					break;
				}
			}
			int baseX = (int)Math.Floor(Math.Floor(lon / tileWidth) * tileWidth);
			int x = (int)Math.Floor((lon - baseX) / tileWidth);
			int baseY = (int)Math.Floor(lat);
			int y = (int)Math.Truncate((lat - baseY) * 8);
			return ((baseX + 180) << 14) + ((baseY + 90) << 6) + (y << 3) + x;
		}
		;
	}

	/// <summary>
	/// Gets the bounding box of a terrasync tile containing the given coordinates
	/// </summary>
	/// <param name="lat">The latitude of the point</param>
	/// <param name="lon">The longitude of the point</param>
	/// <returns>
	/// A 2x2 array, where the first element is the bottom left corner, and the second element is the top right corner.
	/// </returns>
	public static double[,] GetTileBoundingBox(double lat, double lon)
	{
		double[,] bbox = { { 0, 0 }, { 0, 0 } };

		if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
		{
			Console.WriteLine("Latitude or longitude out of range");
		}
		else
		{
			double lookup = Math.Abs(lat);
			double tileWidth = 0;
			for (int i = 0; i < 7; i++)
			{
				if (lookup >= LatitudeIndex[i, 0])
				{
					tileWidth = LatitudeIndex[i, 1];
					break;
				}
			}
			bbox[0, 0] = Math.Floor(lat / 0.125) * 0.125;
			bbox[0, 1] = Math.Floor(lon / tileWidth) * tileWidth;
			bbox[1, 0] = (Math.Floor(lat / 0.125) * 0.125) + 0.125;
			bbox[1, 1] = (Math.Floor(lon / tileWidth) * tileWidth) + tileWidth;
		}
		;
		return bbox;
	}

	public static List<(double, double)> GetTilesWithinRadius(double lat, double lon, double radiusMiles)
	{
		List<(double, double)> result = [];

		double[,] tileBox = GetTileBoundingBox(lat, lon);
		double tileCenterLat = (tileBox[0, 0] + tileBox[1, 0]) / 2;
		double tileCenterLon = (tileBox[0, 1] + tileBox[1, 1]) / 2;

		double earthCircumference = 24880.598;
		double latMin = tileCenterLat - (radiusMiles / earthCircumference * 360);
		double latMax = tileCenterLat + (radiusMiles / earthCircumference * 360);
		double lonMin = tileCenterLon - (radiusMiles / (earthCircumference * Math.Cos(tileCenterLat * Math.PI / 180)) * 360);
		double lonMax = tileCenterLon + (radiusMiles / (earthCircumference * Math.Cos(tileCenterLat * Math.PI / 180)) * 360);

		for (double i = latMin; i <= latMax; i += 0.125)
		{
			double lookup = Math.Abs(i);
			double step = 0;
			for (int j = 0; j < 7; j++)
			{
				if (lookup >= LatitudeIndex[j, 0])
				{
					step = LatitudeIndex[j, 1];
					break;
				}
			}
			for (double j = lonMin; j <= lonMax; j += step)
			{
				double[,] currentTileBox = GetTileBoundingBox(i, j);
				double currentCenterLat = (currentTileBox[0, 0] + currentTileBox[1, 0]) / 2;
				double currentCenterLon = (currentTileBox[0, 1] + currentTileBox[1, 1]) / 2;
				if (Haversine(currentCenterLat, tileCenterLat, currentCenterLon, tileCenterLon) <= radiusMiles)
				{
					result.Add((currentCenterLat, currentCenterLon));
				}
			}
		}
		return result;
	}

	/// <summary>
	/// Calculates the distance between two points on the surface of the earth.
	/// </summary>
	/// <param name="lat1">The latitude of the first point in degrees.</param>
	/// <param name="lat2">The latitude of the second point in degrees.</param>
	/// <param name="lon1">The longitude of the first point in degrees.</param>
	/// <param name="lon2">The longitude of the second point in degrees.</param>
	/// <returns>The distance between the two points in miles.</returns>
	/// <remarks>
	/// Uses the haversine formula to calculate the distance between two points on the surface of the earth.
	/// </remarks>
	public static double Haversine(double lat1Deg, double lat2Deg, double lon1Deg, double lon2Deg)
	{
		const double r = 3963.19; // miles

		double lat1 = lat1Deg * Math.PI / 180.0;
		double lat2 = lat2Deg * Math.PI / 180.0;
		double lon1 = lon1Deg * Math.PI / 180.0;
		double lon2 = lon2Deg * Math.PI / 180.0;

		var sdlat = Math.Sin((lat2 - lat1) / 2);
		var sdlon = Math.Sin((lon2 - lon1) / 2);
		var q = sdlat * sdlat + Math.Cos(lat1) * Math.Cos(lat2) * sdlon * sdlon;
		var d = 2 * r * Math.Asin(Math.Sqrt(q));
		return d;
	}

	/// <summary>
	/// Crops the given image by the specified amount on the top and bottom, and resizes it to the original size.
	/// </summary>
	/// <param name="cropYTop">The number of pixels to remove from the top.</param>
	/// <param name="cropYBottom">The number of pixels to remove from the bottom.</param>
	/// <param name="name">The name of the file to be cropped.</param>
	/// <remarks>
	/// The image is first cropped, then resized to the original size.
	/// The cropping area is defined as a rectangle with the top left corner at (0, <paramref name="cropYTop"/>) and the bottom right corner at (<paramref name="name"/>'s width, <paramref name="name"/>'s height - <paramref name="cropYBottom"/>).
	/// </remarks>
	static void ImageCrop(int cropYTop, int cropYBottom, string name)
	{
		using (SKBitmap original = SKBitmap.Decode(name))
		{
			// Define crop area
			int cropWidth = original.Width - 100, cropHeight = original.Height - 100;

			using SKBitmap cropped = new(cropWidth, cropHeight);
			using (SKCanvas canvas = new(cropped))
			{
				canvas.DrawBitmap(original, new SKRect(0, cropYTop, original.Width, original.Height - (cropYTop + cropYBottom)), new SKRect(0, 0, cropWidth, cropHeight));
			}

			using SKBitmap resized = cropped.Resize(new SKImageInfo(original.Width, original.Height), SKSamplingOptions.Default);
			using var image = SKImage.FromBitmap(resized);
			using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
			using var stream = System.IO.File.OpenWrite(name);
			data.SaveTo(stream);
		}

		Console.WriteLine("Image cropped and resized successfully.");
	}

	/// <summary>
	/// Downloads terrain, orthophoto and OpenStreetMap data related to a geographic tile at specified latitude and longitude.
	/// </summary>
	/// <param name="lat">The latitude of the tile's center.</param>
	/// <param name="lon">The longitude of the tile's center.</param>
	/// <param name="size">The size of the tile.</param>
	/// <param name="version">The version of the data to be downloaded.</param>
	/// <remarks>
	/// This method downloads terrain data, orthophoto imagery, object data, and OpenStreetMap data for the specified tile.
	/// It handles SSL certificate errors due to self-signed certificates.
	/// </remarks>
	static async Task DownloadTile(double lat, double lon, int size, string version)
	{
		// This has to ignore SSL certificate errors, because the server is self-signed
		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };

		// Main HTTP request - this is where everything important has to happen
		using HttpClient client = new(handler);
		try
		{
			await DownloadTerrain(lat, lon, version);
			await DownloadOrthophoto(lat, lon, size);
			await DownloadObjects(lat, lon, version);
			await DownloadOSM(lat, lon);

			Console.WriteLine($"Download successful.");
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex);
			Console.WriteLine($"Error downloading tile: {ex.Message}");
		}
	}

	static async Task DownloadTerrain(double lat, double lon, string version)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		string urlBtg = TerrServerUrl + version + "/Terrain/" + subfolder + tile + ".btg.gz";
		string urlStg = TerrServerUrl + version + "/Terrain/" + subfolder + tile + ".stg";

		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		if (version == "ws2")
		{
			if (CurrentTasks.Add(urlBtg))
			{
				try
				{
					Console.WriteLine("Downloading " + urlBtg + " ...");
					byte[] btgBytes = await client.GetByteArrayAsync(urlBtg);
					if (!Directory.Exists(SavePath + "Terrain/" + subfolder))
					{
						_ = Directory.CreateDirectory(SavePath + "Terrain/" + subfolder);
						Console.WriteLine("Creating terrain directories...");
					}
					await File.WriteAllBytesAsync(SavePath + "Terrain/" + subfolder + tile + ".btg.gz", btgBytes);
					_ = CurrentTasks.Remove(urlBtg);
				}
				catch (HttpRequestException ex)
				{
					Console.WriteLine("Error downloading file(" + urlBtg + "): " + ex.Message);
					_ = CurrentTasks.Remove(urlBtg);
				}
			}

			if (CurrentTasks.Add(urlStg))
			{
				try
				{
					Console.WriteLine("Downloading " + urlStg + " ...");
					byte[] stgBytes = await client.GetByteArrayAsync(urlStg);
					if (!Directory.Exists(SavePath + "Terrain/" + subfolder))
					{
						_ = Directory.CreateDirectory(SavePath + "Terrain/" + subfolder);
						Console.WriteLine("Creating terrain directories...");
					}
					Console.WriteLine(SavePath + "Terrain/" + subfolder + tile + ".stg");
					await File.WriteAllBytesAsync(SavePath + "Terrain/" + subfolder + tile + ".stg", stgBytes);
					string[] stgFile = System.Text.Encoding.UTF8.GetString(stgBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
					foreach (string line in stgFile)
					{
						string[] tokens = line.Split(' ');
						if (tokens[0] == "OBJECT")
						{
							_ = DownloadAirport(tokens[1].Split(".")[0], version);
							string urlObj = TerrServerUrl + version + "/Terrain/" + subfolder + tokens[1] + ".gz";
							if (CurrentTasks.Add(urlObj))
							{
								Console.WriteLine("Downloading " + urlObj + " ...");
								byte[] objectBytes = await client.GetByteArrayAsync(urlObj);
								try { await File.WriteAllBytesAsync(SavePath + "/Terrain/" + subfolder + tokens[1] + ".gz", objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlObj);
							}
						}
						else if (tokens[0] == "OBJECT_SHARED")
						{
							if (!Directory.Exists(Path.GetDirectoryName(SavePath + "/" + tokens[1])))
							{
								_ = Directory.CreateDirectory(Path.GetDirectoryName(SavePath + "/" + tokens[1]));
							}

							string acFile;
							string urlXml = TerrServerUrl + version + "/" + tokens[1];
							if (CurrentTasks.Add(urlXml))
							{
								if (tokens[1].EndsWith(".xml"))
								{
									Console.WriteLine("Downloading " + urlXml + " ...");
									byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
									try { await File.WriteAllBytesAsync(SavePath + "/" + tokens[1], objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
									_ = CurrentTasks.Remove(urlXml);
									string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
									acFile = Path.GetDirectoryName(tokens[1]).Replace("\\", "/") + "/" + objectFile.Substring(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6);
								}
								else
								{
									acFile = tokens[1];
								}
								string urlAc = TerrServerUrl + version + "/" + acFile;
								if (CurrentTasks.Add(urlAc))
								{
									Console.WriteLine("Downloading " + urlAc + " ...");
									byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
									try { await File.WriteAllBytesAsync(SavePath + "/" + acFile, modelBytes); } catch (IOException) { Console.WriteLine("RACE"); }
									_ = CurrentTasks.Remove(urlAc);
									string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
									foreach (string modelLine in modelFile)
									{
										if (modelLine.StartsWith("texture "))
										{
											string urlTex = TerrServerUrl + version + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", "");
											if (CurrentTasks.Add(urlTex))
											{
												Console.WriteLine("Downloading " + urlTex + " ...");
												byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
												try { await File.WriteAllBytesAsync(SavePath + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { Console.WriteLine("RACE"); }
												_ = CurrentTasks.Remove(urlTex);
											}
										}
									}
								}
							}
						}
					}
				}
				catch (HttpRequestException ex)
				{
					Console.WriteLine("Error downloading file(" + urlStg + "): " + ex.Message);
				}
				_ = CurrentTasks.Remove(urlStg);
			}
		}
		else if (version == "ws3")
		{

		}
		else
		{

		}
	}

	static async Task DownloadAirport(string code, string version)
	{
		string subfolder = "Airports/" + string.Join("/", code[..^1].ToCharArray());
		string parent = TerrServerUrl + version + "/" + subfolder;
		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		try
		{
			HtmlDocument airportParentDir = new();
			airportParentDir.LoadHtml(System.Text.Encoding.UTF8.GetString(client.GetByteArrayAsync(parent).Result));
			foreach (HtmlNode node in airportParentDir.DocumentNode.SelectNodes("//tr"))
			{
				if (!node.InnerHtml.Contains(".xml"))
					continue;

				HtmlDocument innerNode = new();
				innerNode.LoadHtml(node.OuterHtml); // For some reason, simply using the node in the foreach loop returns weird and incorrect results
				string airportAttribute = innerNode.DocumentNode.SelectSingleNode("//a").InnerHtml;
				if (airportAttribute.Split(".")[0] == code)
				{
					string urlApt = parent + "/" + airportAttribute;
					if (CurrentTasks.Add(urlApt))
					{
						Console.WriteLine("Downloading " + urlApt + " ...");
						byte[] arptBytes = await client.GetByteArrayAsync(urlApt);
						if (!Directory.Exists(SavePath + subfolder))
						{
							_ = Directory.CreateDirectory(SavePath + subfolder);
							Console.WriteLine("Creating airport directories...");
						}
						await File.WriteAllBytesAsync(SavePath + subfolder + "/" + airportAttribute, arptBytes);
						_ = CurrentTasks.Remove(urlApt);
					}
				}
			}
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error accessing airport data: " + ex.Message);
		}
		;
	}

	static async Task DownloadOrthophoto(double lat, double lon, int size)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		double[,] bbox = GetTileBoundingBox(lat, lon);
		double[,] bboxMercator = { { 0, 0 }, { 0, 0 } };
		bboxMercator[0, 0] = Math.Log(Math.Tan((90.0 + bbox[0, 0]) * Math.PI / 360.0)) * 6378137;
		bboxMercator[0, 1] = bbox[0, 1] * Math.PI * 6378137 / 180;
		bboxMercator[1, 0] = Math.Log(Math.Tan((90.0 + bbox[1, 0]) * Math.PI / 360.0)) * 6378137;
		bboxMercator[1, 1] = bbox[1, 1] * Math.PI * 6378137 / 180;
		int crop = (OrthoRes - (int)Math.Abs((bboxMercator[0, 0] - bboxMercator[1, 0]) / (bboxMercator[0, 1] - bboxMercator[1, 1]) * OrthoRes)) / 2;

		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		string urlPic = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox=" + bboxMercator[0, 1] + "%2C" + bboxMercator[0, 0] + "%2C" + bboxMercator[1, 1] + "%2C" + bboxMercator[1, 0] + "&bboxSR=&layers=&layerDefs=&size=" + size + "%2C" + size + "&imageSR=&historicMoment=&format=jpg&transparent=false&dpi=&time=&timeRelation=esriTimeRelationOverlaps&layerTimeOptions=&dynamicLayers=&gdbVersion=&mapScale=&rotation=&datumTransformations=&layerParameterValues=&mapRangeValues=&layerRangeValues=&clipping=&spatialFilter=&f=image";
		try
		{
			if (CurrentTasks.Add(urlPic))
			{
				Console.WriteLine("Downloading " + urlPic + " ...");
				byte[] orthoBytes = await client.GetByteArrayAsync(urlPic);
				if (!Directory.Exists(SavePath + "Orthophotos/" + subfolder))
				{
					_ = Directory.CreateDirectory(SavePath + "Orthophotos/" + subfolder);
					Console.WriteLine("Creating orthophoto directories...");
				}
				await File.WriteAllBytesAsync(TempPath + tile + ".jpg", orthoBytes);
				ImageCrop(crop, 0, TempPath + tile + ".jpg");
				ProcessStartInfo startInfo = new()
				{
					FileName = "texconv.exe",
					Arguments = "-f DXT5 -o \"" + SavePath + "Orthophotos/" + subfolder + "\" -w " + size + " -h " + size + " -y \"" + tile + ".jpg\"",
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using (Process process = new()
				{ StartInfo = startInfo })
				{
					_ = process.Start();
					string result = process.StandardOutput.ReadToEnd();
					process.WaitForExit();
					Console.WriteLine(result);
				}
				File.Delete(TempPath + tile + ".jpg");
				_ = CurrentTasks.Remove(urlPic);
			}
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error downloading image(" + urlPic + "): " + ex.Message);
		}
	}

	static async Task DownloadObjects(double lat, double lon, string version)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		string urlObj = TerrServerUrl + version + "/Objects/" + subfolder + tile + ".stg";

		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		if (CurrentTasks.Add(urlObj))
		{
			try
			{
				Console.WriteLine("Downloading " + urlObj + " ...");
				byte[] objBytes = await client.GetByteArrayAsync(urlObj);
				if (!Directory.Exists(SavePath + "Objects/" + subfolder))
				{
					_ = Directory.CreateDirectory(SavePath + "Objects/" + subfolder);
					Console.WriteLine("Creating object directories...");
				}
				await File.WriteAllBytesAsync(SavePath + "Objects/" + subfolder + tile + ".stg", objBytes);
				string[] objFile = System.Text.Encoding.UTF8.GetString(objBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
				foreach (string line in objFile)
				{
					string[] tokens = line.Split(' ');
					if (tokens[0] == "OBJECT_SHARED")
					{
						if (!Directory.Exists(Path.GetDirectoryName(SavePath + "/" + tokens[1])))
						{
							_ = Directory.CreateDirectory(Path.GetDirectoryName(SavePath + "/" + tokens[1]));
						}

						string acFile;
						string urlXml = TerrServerUrl + version + "/" + tokens[1];
						if (CurrentTasks.Add(urlXml))
						{
							if (tokens[1].EndsWith(".xml"))
							{
								Console.WriteLine("Downloading " + urlXml + " ...");
								byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
								try { await File.WriteAllBytesAsync(SavePath + "/" + tokens[1], objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlXml);
								string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
								acFile = Path.GetDirectoryName(tokens[1]).Replace("\\", "/") + "/" + objectFile.Substring(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6);
							}
							else
							{
								acFile = tokens[1];
							}
							string urlAc = TerrServerUrl + version + "/" + acFile;
							if (CurrentTasks.Add(urlAc))
							{
								Console.WriteLine("Downloading " + urlAc + " ...");
								byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
								try { await File.WriteAllBytesAsync(SavePath + "/" + acFile, modelBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlAc);
								string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
								foreach (string modelLine in modelFile)
								{
									if (modelLine.StartsWith("texture "))
									{
										string urlTex = TerrServerUrl + version + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", "");
										if (CurrentTasks.Add(urlTex))
										{
											Console.WriteLine("Downloading " + urlTex + " ...");
											byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
											try { await File.WriteAllBytesAsync(SavePath + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { Console.WriteLine("RACE"); }
											_ = CurrentTasks.Remove(urlTex);
										}
									}
								}
							}
						}
					}
					else if (tokens[0] == "OBJECT_STATIC")
					{
						string acFile;
						string urlXml = TerrServerUrl + version + "/Objects/" + subfolder + tokens[1];
						if (CurrentTasks.Add(urlXml))
						{
							if (tokens[1].EndsWith(".xml"))
							{
								Console.WriteLine("Downloading " + urlXml + " ...");
								byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
								try { await File.WriteAllBytesAsync(SavePath + "/Objects/" + subfolder + tokens[1], objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlXml);
								string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
								acFile = Path.GetDirectoryName(tokens[1]).Replace("\\", "/") + objectFile.Substring(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6);
							}
							else
							{
								acFile = tokens[1];
							}
							string urlAc = TerrServerUrl + version + "/Objects/" + subfolder + acFile;
							if (CurrentTasks.Add(urlAc))
							{
								Console.WriteLine("Downloading " + urlAc + " ...");
								byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
								try { await File.WriteAllBytesAsync(SavePath + "/Objects/" + subfolder + acFile, modelBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlAc);
								string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
								foreach (string modelLine in modelFile)
								{
									if (modelLine.StartsWith("texture "))
									{
										string urlTex = TerrServerUrl + version + "/" + "/Objects/" + subfolder + modelLine[8..].Replace("\"", "");
										if (CurrentTasks.Add(urlTex))
										{
											Console.WriteLine("Downloading " + urlTex + " ...");
											byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
											try { await File.WriteAllBytesAsync(SavePath + "Objects/" + subfolder + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { Console.WriteLine("RACE"); }
											_ = CurrentTasks.Remove(urlTex);
										}
									}
								}
							}
						}
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file(" + urlObj + "): " + ex.Message);
			}
		}
	}

	static async Task DownloadOSM(double lat, double lon)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0');
		string subfolderTxz = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/";
		string urlBuildings = TerrServerUrl + "osm2city/Buildings/" + subfolder + ".txz";
		string urlDetails = TerrServerUrl + "osm2city/Details/" + subfolder + ".txz";
		string urlPylons = TerrServerUrl + "osm2city/Pylons/" + subfolder + ".txz";
		string urlRoads = TerrServerUrl + "osm2city/Roads/" + subfolder + ".txz";
		string urlTrees = TerrServerUrl + "osm2city/Trees/" + subfolder + ".txz";
		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		client.Timeout = new TimeSpan(0, 10, 0);
		if (CurrentTasks.Add(urlBuildings))
		{
			try
			{
				Console.WriteLine("Downloading " + urlBuildings + " ...");
				byte[] buildingsBytes = await client.GetByteArrayAsync(urlBuildings);
				if (!Directory.Exists(SavePath + "Buildings/" + subfolder))
				{
					_ = Directory.CreateDirectory(SavePath + "Buildings/" + subfolder);
					Console.WriteLine("Creating OSM building directories...");
				}
				await File.WriteAllBytesAsync(SavePath + "Buildings/" + subfolder + ".txz", buildingsBytes);
				Console.WriteLine("Unzipping " + SavePath + "Buildings/" + subfolder + ".txz ...");
				using FileStream xzStream = File.OpenRead(SavePath + "Buildings/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using var tarFileStream = File.Create(SavePath + "Buildings/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file (" + urlBuildings + "): " + ex.Message);
			}
			_ = CurrentTasks.Remove(urlBuildings);
		}
		if (CurrentTasks.Add(urlDetails))
		{
			try
			{
				Console.WriteLine("Downloading " + urlDetails + " ...");
				byte[] detailsBytes = await client.GetByteArrayAsync(urlDetails);
				if (!Directory.Exists(SavePath + "Details/" + subfolder))
				{
					_ = Directory.CreateDirectory(SavePath + "Details/" + subfolder);
					Console.WriteLine("Creating OSM detail directories...");
				}
				await File.WriteAllBytesAsync(SavePath + "Details/" + subfolder + ".txz", detailsBytes);
				Console.WriteLine("Unzipping " + SavePath + "Details/" + subfolder + ".txz ...");
				using var xzStream = File.OpenRead(SavePath + "Details/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using var tarFileStream = File.Create(SavePath + "Details/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file (" + urlDetails + "): " + ex.Message);
			}
			_ = CurrentTasks.Remove(urlBuildings);
		}
		if (CurrentTasks.Add(urlPylons))
		{
			try
			{
				Console.WriteLine("Downloading " + urlPylons + " ...");
				byte[] pylonsBytes = await client.GetByteArrayAsync(urlPylons);
				if (!Directory.Exists(SavePath + "Pylons/" + subfolder))
				{
					_ = Directory.CreateDirectory(SavePath + "Pylons/" + subfolder);
					Console.WriteLine("Creating OSM pylon directories...");
				}
				await File.WriteAllBytesAsync(SavePath + "Pylons/" + subfolder + ".txz", pylonsBytes);
				Console.WriteLine("Unzipping " + SavePath + "Pylons/" + subfolder + ".txz ...");
				using var xzStream = File.OpenRead(SavePath + "Pylons/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using var tarFileStream = File.Create(SavePath + "Pylons/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file (" + urlPylons + "): " + ex.Message);
			}
			_ = CurrentTasks.Remove(urlPylons);
		}
		if (CurrentTasks.Add(urlRoads))
		{
			try
			{
				Console.WriteLine("Downloading " + urlRoads + " ...");
				byte[] roadsBytes = await client.GetByteArrayAsync(urlRoads);
				if (!Directory.Exists(SavePath + "Roads/" + subfolder))
				{
					_ = Directory.CreateDirectory(SavePath + "Roads/" + subfolder);
					Console.WriteLine("Creating OSM road directories...");
				}
				await File.WriteAllBytesAsync(SavePath + "Roads/" + subfolder + ".txz", roadsBytes);
				Console.WriteLine("Unzipping " + SavePath + "Roads/" + subfolder + ".txz ...");
				using var xzStream = File.OpenRead(SavePath + "Roads/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using var tarFileStream = File.Create(SavePath + "Roads/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file (" + urlRoads + "): " + ex.Message);
			}
			_ = CurrentTasks.Remove(urlRoads);
		}
		if (CurrentTasks.Add(urlTrees))
		{
			try
			{
				Console.WriteLine("Downloading " + urlTrees + " ...");
				byte[] treesBytes = await client.GetByteArrayAsync(urlTrees);
				if (!Directory.Exists(SavePath + "Trees/" + subfolder))
				{
					_ = Directory.CreateDirectory(SavePath + "Trees/" + subfolder);
					Console.WriteLine("Creating OSM tree directories...");
				}
				await File.WriteAllBytesAsync(SavePath + "Trees/" + subfolder + ".txz", treesBytes);
				Console.WriteLine("Unzipping " + SavePath + "Trees/" + subfolder + ".txz ...");
				using var xzStream = File.OpenRead(SavePath + "Trees/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using var tarFileStream = File.Create(SavePath + "Trees/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file (" + urlTrees + "): " + ex.Message);
			}
			_ = CurrentTasks.Remove(urlTrees);
		}
	}

	static async Task DownloadPlan(string filepath, int radius)
	{
		HashSet<(double, double)> tiles = [];
		if (filepath.EndsWith(".fgfp"))
		{
			XmlDocument plan = new();
			plan.Load(filepath);
			XmlNodeList waypoints = plan.GetElementsByTagName("wp");
			for (int i = 0; i < waypoints.Count - 1; i++)
			{
				int childCountCur = waypoints[i].ChildNodes.Count;
				double wpCurLat = Convert.ToDouble(waypoints[i].ChildNodes[childCountCur - 2].InnerText);
				double wpCurLon = Convert.ToDouble(waypoints[i].ChildNodes[childCountCur - 1].InnerText);
				int childCountNxt = waypoints[i + 1].ChildNodes.Count;
				double wpNxtLat = Convert.ToDouble(waypoints[i + 1].ChildNodes[childCountNxt - 2].InnerText);
				double wpNxtLon = Convert.ToDouble(waypoints[i + 1].ChildNodes[childCountNxt - 1].InnerText);
				List<(double, double)> pointsAlongPath = GreatCircleInterpolator.GetGreatCirclePoints(wpCurLat, wpCurLon, wpNxtLat, wpNxtLon, (int)Math.Floor(Haversine(wpCurLat, wpNxtLat, wpCurLon, wpNxtLon)) / 5);
				Console.WriteLine("Points along path: " + pointsAlongPath.Count);
				_ = pointsAlongPath.Prepend((wpCurLat, wpCurLon));
				foreach ((double lat, double lon) in pointsAlongPath)
				{
					List<(double, double)> circle = GetTilesWithinRadius(lat, lon, radius);
					foreach ((double latitude, double longitude) in circle)
					{
						tiles.Add((latitude, longitude));
					}
				}
			}
		}
		Console.WriteLine(tiles.Count);
		foreach ((double lat, double lon) in tiles)
		{
			_ = DownloadTile(lat, lon, OrthoRes, "ws2");
		}
	}
}
