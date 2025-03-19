using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mapsui.UI;
using Mapsui.UI.Avalonia;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TerraMaster;

public partial class App : Application
{
	static double[,] LAT_INDEX = { { 89, 12 }, { 86, 4 }, { 83, 2 }, { 76, 1 }, { 62, 0.5 }, { 22, 0.25 }, { 0, 0.125 } };
	string SAVE_PATH = "E:/testing/";
	string TERR_SERVER_URL = "https://terramaster.flightgear.org/terrasync/";
	Dictionary<string, double[]> AIRPORTS = [];
	int ORTHO_RES = 2048;

	public override void Initialize()
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
				AIRPORTS.Add(airportInfo[0], [double.Parse(airportInfo[1]), double.Parse(airportInfo[2])]);
			}
		}
		Console.WriteLine("Done.");
		for (int i = -5; i < 5; i++)
		{
			for (int j = -5; j < 5; j++)
			{
				_ = DownloadTile(AIRPORTS["KSEA"][1] + i * 0.125, AIRPORTS["KSEA"][0] + j * 0.125, ORTHO_RES, "ws2", TERR_SERVER_URL, SAVE_PATH);
			}
		}
		var mapControl = new MapControl();
		mapControl.Map = new Mapsui.Map();
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
			for (int i = 0; i < LAT_INDEX.Length; i++)
			{
				if (lookup >= LAT_INDEX[i, 0])
				{
					tileWidth = LAT_INDEX[i, 1];
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
			for (int i = 0; i < LAT_INDEX.Length; i++)
			{
				if (lookup >= LAT_INDEX[i, 0])
				{
					tileWidth = LAT_INDEX[i, 1];
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

	public static List<double[,]> GetAreaAroundPoint(double lat, double lon) {
		List<double[,]> tiles = [];
		return tiles;
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

	static async Task DownloadTile(double lat, double lon, int size, string version, string server, string path)
	{
		// This has to ignore SSL certificate errors, because the server is self-signed
		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };

		// Main HTTP request - this is where everything important has to happen
		using HttpClient client = new(handler);
		try
		{
			await DownloadTerrain(lat, lon, version, server, path);
			await DownloadOrthophoto(lat, lon, size, path);
			await DownloadObjects(lat, lon, version, server, path);
			await DownloadOSM(lat, lon, server, path);

			Console.WriteLine($"Download successful.");
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex);
			Console.WriteLine($"Error downloading file: {ex.Message}");
		}
	}

	static async Task DownloadTerrain(double lat, double lon, string version, string server, string path)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		string urlBtg = server + version + "/Terrain/" + subfolder + tile + ".btg.gz";
		string urlStg = server + version + "/Terrain/" + subfolder + tile + ".stg";

		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		if (version == "ws2")
		{
			try
			{
				Console.WriteLine("Downloading " + urlBtg + " ...");
				byte[] btgBytes = await client.GetByteArrayAsync(urlBtg);
				if (!Directory.Exists(path + "Terrain/" + subfolder))
				{
					Directory.CreateDirectory(path + "Terrain/" + subfolder);
					Console.WriteLine("Creating terrain directories...");
				}
				await File.WriteAllBytesAsync(path + "Terrain/" + subfolder + tile + ".btg.gz", btgBytes);
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file(" + urlBtg + "): " + ex.Message);
			}

			try
			{
				Console.WriteLine("Downloading " + urlStg + " ...");
				byte[] stgBytes = await client.GetByteArrayAsync(urlStg);
				await File.WriteAllBytesAsync(path + "Terrain/" + subfolder + tile + ".stg", stgBytes);
				string[] stgFile = System.Text.Encoding.UTF8.GetString(stgBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
				HashSet<string> secondaryLinksStg = [];
				foreach (string line in stgFile)
				{
					string[] tokens = line.Split(' ');
					if (tokens[0] == "OBJECT")
					{
						Console.WriteLine(server + version + "/Terrain/" + subfolder + tokens[1] + ".gz");
						byte[] objectBytes = await client.GetByteArrayAsync(server + version + "/Terrain/" + subfolder + tokens[1] + ".gz");
						try { await File.WriteAllBytesAsync(path + "/Terrain/" + subfolder + tokens[1] + ".gz", objectBytes); } catch (IOException) { continue; }
					}
					else if (tokens[0] == "OBJECT_SHARED" && secondaryLinksStg.Add(tokens[1]))
					{
						if (!Directory.Exists(Path.GetDirectoryName(path + "/" + tokens[1])))
						{
							_ = Directory.CreateDirectory(Path.GetDirectoryName(path + "/" + tokens[1]));
						}

						string acFile;
						if (tokens[1].EndsWith(".xml"))
						{
							byte[] objectBytes = await client.GetByteArrayAsync(server + version + "/" + tokens[1]);
							try { await File.WriteAllBytesAsync(path + "/" + tokens[1], objectBytes); } catch (IOException) { continue; }
							string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
							acFile = Path.GetDirectoryName(tokens[1]).Replace("\\", "/") + "/" + objectFile.Substring(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6);
						}
						else
						{
							acFile = tokens[1];
						}
						byte[] modelBytes = await client.GetByteArrayAsync(server + version + "/" + acFile);
						try { await File.WriteAllBytesAsync(path + "/" + acFile, modelBytes); } catch (IOException) { continue; }
						string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
						foreach (string modelLine in modelFile)
						{
							if (modelLine.StartsWith("texture "))
							{
								byte[] textureBytes = await client.GetByteArrayAsync(server + version + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""));
								try { await File.WriteAllBytesAsync(path + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { continue; }
							}
						}
					}
				}
			}
			catch (HttpRequestException ex)
			{
				Console.WriteLine("Error downloading file(" + urlStg + "): " + ex.Message);
			}
		}
		else if (version == "ws3")
		{

		}
		else
		{

		}
	}

	static async Task DownloadOrthophoto(double lat, double lon, int size, string path)
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
		int crop = (2048 - (int)Math.Abs((bboxMercator[0, 0] - bboxMercator[1, 0]) / (bboxMercator[0, 1] - bboxMercator[1, 1]) * 2048)) / 2;

		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		try
		{
			Console.WriteLine("Downloading https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox=" + bboxMercator[0, 1] + "%2C" + bboxMercator[0, 0] + "%2C" + bboxMercator[1, 1] + "%2C" + bboxMercator[1, 0] + "&bboxSR=&layers=&layerDefs=&size=" + size + "%2C" + size + "&imageSR=&historicMoment=&format=jpg&transparent=false&dpi=&time=&timeRelation=esriTimeRelationOverlaps&layerTimeOptions=&dynamicLayers=&gdbVersion=&mapScale=&rotation=&datumTransformations=&layerParameterValues=&mapRangeValues=&layerRangeValues=&clipping=&spatialFilter=&f=image ...");
			byte[] orthoBytes = await client.GetByteArrayAsync("https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox=" + bboxMercator[0, 1] + "%2C" + bboxMercator[0, 0] + "%2C" + bboxMercator[1, 1] + "%2C" + bboxMercator[1, 0] + "&bboxSR=&layers=&layerDefs=&size=" + size + "%2C" + size + "&imageSR=&historicMoment=&format=jpg&transparent=false&dpi=&time=&timeRelation=esriTimeRelationOverlaps&layerTimeOptions=&dynamicLayers=&gdbVersion=&mapScale=&rotation=&datumTransformations=&layerParameterValues=&mapRangeValues=&layerRangeValues=&clipping=&spatialFilter=&f=image");
			if (!Directory.Exists(path + "Orthophotos/" + subfolder))
			{
				Directory.CreateDirectory(path + "Orthophotos/" + subfolder);
				Console.WriteLine("Creating orthophoto directories...");
			}
			await File.WriteAllBytesAsync(tile + ".jpg", orthoBytes);
			ImageCrop(crop, 0, tile + ".jpg");
			ProcessStartInfo startInfo = new()
			{
				FileName = "texconv.exe",
				Arguments = "-f DXT5 -o \"" + path + "Orthophotos/" + subfolder + "\" -w " + size + " -h " + size + " -y \"" + tile + ".jpg\"",
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using (Process process = new()
			{ StartInfo = startInfo })
			{
				process.Start();
				string result = process.StandardOutput.ReadToEnd();
				process.WaitForExit();
				Console.WriteLine(result);
			}
			File.Delete(tile + ".jpg");
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error downloading image(https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox=" + bboxMercator[0, 1] + "%2C" + bboxMercator[0, 0] + "%2C" + bboxMercator[1, 1] + "%2C" + bboxMercator[1, 0] + "&bboxSR=&layers=&layerDefs=&size=" + size + "%2C" + size + "&imageSR=&historicMoment=&format=jpg&transparent=false&dpi=&time=&timeRelation=esriTimeRelationOverlaps&layerTimeOptions=&dynamicLayers=&gdbVersion=&mapScale=&rotation=&datumTransformations=&layerParameterValues=&mapRangeValues=&layerRangeValues=&clipping=&spatialFilter=&f=image ): " + ex.Message);
		}
	}

	static async Task DownloadObjects(double lat, double lon, string version, string server, string path)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		string urlObj = server + version + "/Objects/" + subfolder + tile + ".stg";

		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		try
		{
			Console.WriteLine("Downloading " + urlObj + " ...");
			byte[] objBytes = await client.GetByteArrayAsync(urlObj);
			if (!Directory.Exists(path + "Objects/" + subfolder))
			{
				Directory.CreateDirectory(path + "Objects/" + subfolder);
				Console.WriteLine("Creating object directories...");
			}
			await File.WriteAllBytesAsync(path + "Objects/" + subfolder + tile + ".stg", objBytes);
			string[] objFile = System.Text.Encoding.UTF8.GetString(objBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
			HashSet<string> secondaryLinksObj = [];
			foreach (string line in objFile)
			{
				string[] tokens = line.Split(' ');
				if (tokens[0] == "OBJECT_SHARED" && secondaryLinksObj.Add(tokens[1]))
				{
					if (!Directory.Exists(Path.GetDirectoryName(path + "/" + tokens[1])))
					{
						_ = Directory.CreateDirectory(Path.GetDirectoryName(path + "/" + tokens[1]));
					}

					string acFile;
					if (tokens[1].EndsWith(".xml"))
					{
						byte[] objectBytes = await client.GetByteArrayAsync(server + version + "/" + tokens[1]);
						try { await File.WriteAllBytesAsync(path + "/" + tokens[1], objectBytes); } catch (IOException) { continue; }
						string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
						acFile = Path.GetDirectoryName(tokens[1]).Replace("\\", "/") + "/" + objectFile.Substring(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6);
						Console.WriteLine(acFile);
					}
					else
					{
						acFile = tokens[1];
					}
					Console.WriteLine(server + version + "/" + acFile);
					byte[] modelBytes = await client.GetByteArrayAsync(server + version + "/" + acFile);
					try { await File.WriteAllBytesAsync(path + "/" + acFile, modelBytes); } catch (IOException) { continue; }
					string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
					foreach (string modelLine in modelFile)
					{
						if (modelLine.StartsWith("texture "))
						{
							Console.WriteLine(server + version + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""));
							byte[] textureBytes = await client.GetByteArrayAsync(server + version + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""));
							try { await File.WriteAllBytesAsync(path + "/" + Path.GetDirectoryName(acFile).Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { continue; }
						}
					}
				}
				else if (tokens[0] == "OBJECT_STATIC" && secondaryLinksObj.Add(tokens[1]))
				{
					string acFile;
					if (tokens[1].EndsWith(".xml"))
					{
						Console.WriteLine(server + version + "/Objects/" + subfolder + tokens[1]);
						byte[] objectBytes = await client.GetByteArrayAsync(server + version + "/Objects/" + subfolder + tokens[1]);
						try { await File.WriteAllBytesAsync(path + "/Objects/" + subfolder + tokens[1], objectBytes); } catch (IOException) { continue; }
						string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
						acFile = Path.GetDirectoryName(tokens[1]).Replace("\\", "/") + objectFile.Substring(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6);
					}
					else
					{
						acFile = tokens[1];
					}
					Console.WriteLine(server + version + "/Objects/" + subfolder + acFile);
					byte[] modelBytes = await client.GetByteArrayAsync(server + version + "/Objects/" + subfolder + acFile);
					try { await File.WriteAllBytesAsync(path + "/Objects/" + subfolder + acFile, modelBytes); } catch (IOException) { continue; }
					string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
					foreach (string modelLine in modelFile)
					{
						if (modelLine.StartsWith("texture "))
						{
							Console.WriteLine(server + version + "/" + "/Objects/" + subfolder + modelLine[8..].Replace("\"", ""));
							byte[] textureBytes = await client.GetByteArrayAsync(server + version + "/" + "Objects/" + subfolder + modelLine[8..].Replace("\"", ""));
							Console.WriteLine(path + "Objects/" + subfolder + modelLine[8..].Replace("\"", ""));
							try { await File.WriteAllBytesAsync(path + "Objects/" + subfolder + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { continue; }
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

	static async Task DownloadOSM(double lat, double lon, string server, string path)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0');
		string subfolderTxz = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/";
		string urlBuildings = server + "osm2city/Buildings/" + subfolder + ".txz";
		string urlDetails = server + "osm2city/Details/" + subfolder + ".txz";
		string urlPylons = server + "osm2city/Pylons/" + subfolder + ".txz";
		string urlRoads = server + "osm2city/Roads/" + subfolder + ".txz";
		string urlTrees = server + "osm2city/Trees/" + subfolder + ".txz";
		HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
		using HttpClient client = new(handler);
		client.Timeout = new System.TimeSpan(0, 10, 0);
		try
		{
			Console.WriteLine("Downloading " + urlBuildings + " ...");
			byte[] buildingsBytes = await client.GetByteArrayAsync(urlBuildings);
			if (!Directory.Exists(path + "Buildings/" + subfolder))
			{
				Directory.CreateDirectory(path + "Buildings/" + subfolder);
				Console.WriteLine("Creating OSM building directories...");
			}
			await File.WriteAllBytesAsync(path + "Buildings/" + subfolder + ".txz", buildingsBytes);
			Console.WriteLine("Unzipping " + path + "Buildings/" + subfolder + ".txz ...");
			using var xzStream = File.OpenRead(path + "Buildings/" + subfolder + ".txz");
			using var reader = ReaderFactory.Open(xzStream);
			while (reader.MoveToNextEntry())
			{
				Console.WriteLine(reader.Entry);
				if (!reader.Entry.IsDirectory)
				{
					using (var tarFileStream = File.Create(path + "Buildings/" + subfolderTxz + reader.Entry))
					{
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error downloading file (" + urlBuildings + "): " + ex.Message);
		}
		try
		{
			Console.WriteLine("Downloading " + urlDetails + " ...");
			byte[] detailsBytes = await client.GetByteArrayAsync(urlDetails);
			if (!Directory.Exists(Path.GetDirectoryName(path + "Details/" + subfolder)))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path + "Details/" + subfolder));
				Console.WriteLine("Creating OSM detail directories...");
			}
			await File.WriteAllBytesAsync(path + "Details/" + subfolder + ".txz", detailsBytes);
			Console.WriteLine("Unzipping " + path + "Details/" + subfolder + ".txz ...");
			using var xzStream = File.OpenRead(path + "Details/" + subfolder + ".txz");
			using var reader = ReaderFactory.Open(xzStream);
			while (reader.MoveToNextEntry())
			{
				Console.WriteLine(reader.Entry);
				if (!reader.Entry.IsDirectory)
				{
					using (var tarFileStream = File.Create(path + "Details/" + subfolderTxz + reader.Entry))
					{
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error downloading file (" + urlDetails + "): " + ex.Message);
		}
		try
		{
			Console.WriteLine("Downloading " + urlPylons + " ...");
			byte[] pylonsBytes = await client.GetByteArrayAsync(urlPylons);
			if (!Directory.Exists(Path.GetDirectoryName(path + "Pylons/" + subfolder)))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path + "Pylons/" + subfolder));
				Console.WriteLine("Creating OSM pylon directories...");
			}
			await File.WriteAllBytesAsync(path + "Pylons/" + subfolder + ".txz", pylonsBytes);
			Console.WriteLine("Unzipping " + path + "Pylons/" + subfolder + ".txz ...");
			using var xzStream = File.OpenRead(path + "Pylons/" + subfolder + ".txz");
			using var reader = ReaderFactory.Open(xzStream);
			while (reader.MoveToNextEntry())
			{
				Console.WriteLine(reader.Entry);
				if (!reader.Entry.IsDirectory)
				{
					using (var tarFileStream = File.Create(path + "Pylons/" + subfolderTxz + reader.Entry))
					{
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error downloading file (" + urlPylons + "): " + ex.Message);
		}
		try
		{
			Console.WriteLine("Downloading " + urlRoads + " ...");
			byte[] roadsBytes = await client.GetByteArrayAsync(urlRoads);
			if (!Directory.Exists(Path.GetDirectoryName(path + "Roads/" + subfolder)))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path + "Roads/" + subfolder));
				Console.WriteLine("Creating OSM road directories...");
			}
			await File.WriteAllBytesAsync(path + "Roads/" + subfolder + ".txz", roadsBytes);
			Console.WriteLine("Unzipping " + path + "Roads/" + subfolder + ".txz ...");
			using var xzStream = File.OpenRead(path + "Roads/" + subfolder + ".txz");
			using var reader = ReaderFactory.Open(xzStream);
			while (reader.MoveToNextEntry())
			{
				Console.WriteLine(reader.Entry);
				if (!reader.Entry.IsDirectory)
				{
					using (var tarFileStream = File.Create(path + "Roads/" + subfolderTxz + reader.Entry))
					{
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error downloading file (" + urlRoads + "): " + ex.Message);
		}
		try
		{
			Console.WriteLine("Downloading " + urlTrees + " ...");
			byte[] treesBytes = await client.GetByteArrayAsync(urlTrees);
			if (!Directory.Exists(Path.GetDirectoryName(path + "Trees/" + subfolder)))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path + "Trees/" + subfolder));
				Console.WriteLine("Creating OSM tree directories...");
			}
			await File.WriteAllBytesAsync(path + "Trees/" + subfolder + ".txz", treesBytes);
			Console.WriteLine("Unzipping " + path + "Trees/" + subfolder + ".txz ...");
			using var xzStream = File.OpenRead(path + "Trees/" + subfolder + ".txz");
			using var reader = ReaderFactory.Open(xzStream);
			while (reader.MoveToNextEntry())
			{
				Console.WriteLine(reader.Entry);
				if (!reader.Entry.IsDirectory)
				{
					using (var tarFileStream = File.Create(path + "Trees/" + subfolderTxz + reader.Entry))
					{
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
		}
		catch (HttpRequestException ex)
		{
			Console.WriteLine("Error downloading file (" + urlTrees + "): " + ex.Message);
		}
	}
}