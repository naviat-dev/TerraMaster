using System.Xml;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using HtmlAgilityPack;
using SharpCompress.Readers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace TerraSDM;

public class DownloadMgr
{
	private static readonly HashSet<string> CurrentTasks = [];
	private static readonly SemaphoreSlim taskQueue = new(50);
	private static readonly HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true };
	public static readonly HttpClient client = new(handler);

	public static async Task DownloadTile(double lat, double lon, int size, string version)
	{
		await taskQueue.WaitAsync();
		try
		{
			try
			{
				await DownloadTerrain(lat, lon, version);
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Logger.Debug("DownloadMgr", $"Terrain data not found for tile at {lat}, {lon}. Skipping terrain download.");
					_ = taskQueue.Release();
					return;
				}
				else
				{
					Logger.Debug("DownloadMgr", $"Error downloading terrain data: {ex.Message}");
				}
			}
			await DownloadOrthophoto(lat, lon, size); // There should always be orthophoto data available when there is terrain data, so no try-catch here
			try
			{
				await DownloadObjects(lat, lon);
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Logger.Debug("DownloadMgr", $"Object data not found for tile at {lat}, {lon}. Skipping object download.");
					_ = taskQueue.Release();
					return;
				}
				else
				{
					Logger.Debug("DownloadMgr", $"Error downloading object data: {ex.Message}");
				}
			}
			await DownloadOSM(lat, lon);
			_ = taskQueue.Release();

			Logger.Info("DownloadMgr", $"Download successful for tile at {lat}, {lon}");
		}
		catch (Exception ex)
		{
			Logger.Error("DownloadMgr", $"Error downloading tile at {lat}, {lon}", ex);
		}
		_ = taskQueue.Release();
	}

	private static async Task DownloadTerrain(double lat, double lon, string version)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = Util.GetTileIndex(lat, lon).ToString();
		string subfolder = $"{hemiLon}{(Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0')}{hemiLat}{(Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0')}/{hemiLon}{Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0')}{hemiLat}{Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0')}/";
		string urlStg = $"{Util.TerrServerUrl}{version}/Terrain/{subfolder}{tile}.stg";

		if (version == "ws2")
		{
			string urlBtg = $"{Util.TerrServerUrl}{version}/Terrain/{subfolder}{tile}.btg.gz"; // Only used in WS2
			string Ws2Dir = Path.Combine(Util.SavePath, "ws2");
			if (CurrentTasks.Add(urlBtg))
			{
				try
				{
					Logger.Debug("DownloadMgr", $"Downloading {urlBtg}");
					byte[] btgBytes = await client.GetByteArrayAsync(urlBtg);
					string terrainDir = Path.Combine(Ws2Dir, "Terrain", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar));
					if (!Directory.Exists(terrainDir))
					{
						_ = Directory.CreateDirectory(terrainDir);
						Logger.Debug("DownloadMgr", "Creating terrain directories...");
					}
					await File.WriteAllBytesAsync(Path.Combine(terrainDir, $"{tile}.btg.gz"), btgBytes);
					_ = CurrentTasks.Remove(urlBtg);
				}
				catch (HttpRequestException)
				{
					_ = CurrentTasks.Remove(urlBtg);
					throw; // Rethrow the exception to be caught in the main method
				}
			}

			if (CurrentTasks.Add(urlStg))
			{
				try
				{
					Logger.Debug("DownloadMgr", $"Downloading {urlStg}");
					byte[] stgBytes = await client.GetByteArrayAsync(urlStg);
					string terrainDir = Path.Combine(Ws2Dir, "Terrain", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar));
					if (!Directory.Exists(terrainDir))
					{
						_ = Directory.CreateDirectory(terrainDir);
						Logger.Debug("DownloadMgr", "Creating terrain directories...");
					}
					string stgPath = Path.Combine(terrainDir, $"{tile}.stg");
					Logger.Debug("DownloadMgr", stgPath);
					await File.WriteAllBytesAsync(stgPath, stgBytes);
					string[] stgFile = System.Text.Encoding.UTF8.GetString(stgBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
					foreach (string line in stgFile)
					{
						string[] tokens = line.Split(' ');
						if (tokens[0] == "OBJECT")
						{
							_ = DownloadAirport(tokens[1].Split(".")[0], version);
							string urlObj = $"{Util.TerrServerUrl}{version}/Terrain/{subfolder}{tokens[1]}.gz";
							if (CurrentTasks.Add(urlObj))
							{
								Logger.Debug("DownloadMgr", $"Downloading {urlObj}");
								byte[] objectBytes = await client.GetByteArrayAsync(urlObj);
								string objPath = Path.Combine("ws2", "Terrain", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar), $"{tokens[1]}.gz");
								await StorageHelper.WriteSceneryBytesAsync(objPath, objectBytes);
								_ = CurrentTasks.Remove(urlObj);
							}
						}
						else if (tokens[0] == "OBJECT_SHARED")
						{
							if (!Directory.Exists(Path.GetDirectoryName($"{Util.SavePath}/{tokens[1]}")))
							{
								_ = Directory.CreateDirectory(Path.GetDirectoryName($"{Util.SavePath}/{tokens[1]}") ?? "");
							}

							string acFile;
							string urlXml = $"{Util.TerrServerUrl}{version}/{tokens[1]}";
							if (CurrentTasks.Add(urlXml))
							{
								if (tokens[1].EndsWith(".xml"))
								{
									Logger.Debug("DownloadMgr", $"Downloading {urlXml}");
									byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
									try { await File.WriteAllBytesAsync($"{Util.SavePath}/{tokens[1]}", objectBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
									_ = CurrentTasks.Remove(urlXml);
									string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
									// TODO: replace with proper XML parsing logic
									acFile = $"{(Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/")}/{objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6)}";
								}
								else
								{
									acFile = tokens[1];
								}
								string urlAc = $"{Util.TerrServerUrl}{version}/{acFile}";
								if (CurrentTasks.Add(urlAc))
								{
									Logger.Debug("DownloadMgr", $"Downloading {urlAc}");
									byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
									try { await File.WriteAllBytesAsync($"{Util.SavePath}/{acFile}", modelBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
									_ = CurrentTasks.Remove(urlAc);
									string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
									foreach (string modelLine in modelFile)
									{
										if (modelLine.StartsWith("texture "))
										{
											string urlTex = $"{Util.TerrServerUrl}{version}/{(Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/")}/{modelLine[8..].Replace("\"", "")}";
											if (CurrentTasks.Add(urlTex))
											{
												Logger.Debug("DownloadMgr", $"Downloading {urlTex}");
												byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
												try { await File.WriteAllBytesAsync($"{Util.SavePath}/{(Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/")}/{modelLine[8..].Replace("\"", "")}", textureBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
												_ = CurrentTasks.Remove(urlTex);
											}
										}
									}
								}
							}
						}
					}
				}
				catch (HttpRequestException)
				{
					_ = CurrentTasks.Remove(urlStg);
					throw; // Rethrow the exception to be caught in the main method
				}
				_ = CurrentTasks.Remove(urlStg);
			}
		}
		else if (version == "ws3")
		{
			string urlVpb = $"{Util.TerrServerUrl}{version}/vpb/{subfolder[..^1]}.zip"; // Only used in WS3
			string Ws3Dir = Path.Combine(Util.SavePath, "ws3");
			if (CurrentTasks.Add(urlStg))
			{
				try
				{
					Logger.Debug("DownloadMgr", $"Downloading {urlStg}");
					byte[] stgBytes = await client.GetByteArrayAsync(urlStg);
					string terrainDir = Path.Combine(Ws3Dir, "Terrain", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar));
					if (!Directory.Exists(terrainDir))
					{
						_ = Directory.CreateDirectory(terrainDir);
						Logger.Debug("DownloadMgr", "Creating terrain directories...");
					}
					string stgPath = Path.Combine(terrainDir, $"{tile}.stg");
					Logger.Debug("DownloadMgr", stgPath);
					await File.WriteAllBytesAsync(stgPath, stgBytes);
					string[] stgFile = System.Text.Encoding.UTF8.GetString(stgBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
					foreach (string line in stgFile)
					{
						string[] tokens = line.Split(' ');
						if (tokens[0] == "OBJECT")
						{
							_ = DownloadAirport(tokens[1].Split(".")[0], version);
							string urlObj = $"{Util.TerrServerUrl}{version}/Terrain/{subfolder}{tokens[1]}.gz";
							if (CurrentTasks.Add(urlObj))
							{
								Logger.Debug("DownloadMgr", $"Downloading {urlObj}");
								byte[] objectBytes = await client.GetByteArrayAsync(urlObj);
								try { await File.WriteAllBytesAsync(Path.Combine(Ws3Dir, "Terrain", subfolder, tokens[1] + ".gz"), objectBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
								_ = CurrentTasks.Remove(urlObj);
							}
						}
						else if (tokens[0] == "OBJECT_SHARED")
						{
							if (!Directory.Exists(Path.GetDirectoryName(Path.Combine(Util.SavePath, tokens[1]))))
							{
								_ = Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Util.SavePath, tokens[1])) ?? "");
							}

							string acFile;
							string urlXml = $"{Util.TerrServerUrl}{version}/{tokens[1]}";
							if (CurrentTasks.Add(urlXml))
							{
								if (tokens[1].EndsWith(".xml"))
								{
									Logger.Debug("DownloadMgr", $"Downloading {urlXml}");
									byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
									try { await File.WriteAllBytesAsync(Path.Combine(Util.SavePath, tokens[1]), objectBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
									_ = CurrentTasks.Remove(urlXml);
									string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
									acFile = $"{(Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/")}/{objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6)}";
								}
								else
								{
									acFile = tokens[1];
								}
								string urlAc = $"{Util.TerrServerUrl}{version}/{acFile}";
								if (CurrentTasks.Add(urlAc))
								{
									Logger.Debug("DownloadMgr", $"Downloading {urlAc}");
									byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
									try { await File.WriteAllBytesAsync(Path.Combine(Util.SavePath, acFile), modelBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
									_ = CurrentTasks.Remove(urlAc);
									string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
									foreach (string modelLine in modelFile)
									{
										if (modelLine.StartsWith("texture "))
										{
											string urlTex = $"{Util.TerrServerUrl}{version}/{(Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/")}/{modelLine[8..].Replace("\"", "")}";
											if (CurrentTasks.Add(urlTex))
											{
												Logger.Debug("DownloadMgr", $"Downloading {urlTex}");
												byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
												try { await File.WriteAllBytesAsync(Path.Combine(Util.SavePath, (Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/"), modelLine[8..].Replace("\"", "")), textureBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
												_ = CurrentTasks.Remove(urlTex);
											}
										}
									}
								}
							}
						}
					}
				}
				catch (HttpRequestException)
				{
					_ = CurrentTasks.Remove(urlStg);
					throw; // Rethrow the exception to be caught in the main method
				}
				_ = CurrentTasks.Remove(urlStg);
			}
			if (CurrentTasks.Add(urlVpb))
			{
				try
				{
					Logger.Debug("DownloadMgr", $"Downloading {urlVpb}");
					byte[] vpbBytes = await client.GetByteArrayAsync(urlVpb);
					await File.WriteAllBytesAsync(Path.Combine(Util.TempPath, $"{subfolder[..^1].Split("/")[1]}.zip"), vpbBytes);
					using FileStream zipStream = File.OpenRead(Path.Combine(Util.TempPath, $"{subfolder[..^1].Split("/")[1]}.zip"));
					using var reader = ReaderFactory.Open(zipStream);
					while (reader.MoveToNextEntry())
					{
						if (!reader.Entry.IsDirectory)
						{
							string entryString = reader.Entry.ToString() ?? "";
							string vpbDir;
							if (entryString.Length == 15 || entryString.EndsWith("added"))
							{
								vpbDir = $"{Ws3Dir}vpb/{subfolder}/";
							}
							else
							{
								vpbDir = $"{Ws3Dir}vpb/{subfolder}{entryString[..10]}_root_L0_X0_Y0/";
							}
							if (!Directory.Exists(vpbDir))
							{
								_ = Directory.CreateDirectory(vpbDir);
							}
							using FileStream zipFileStream = File.Create(Path.Combine(vpbDir, entryString));
							reader.WriteEntryTo(zipFileStream);
						}
					}
					_ = CurrentTasks.Remove(urlVpb);
				}
				catch (HttpRequestException)
				{
					_ = CurrentTasks.Remove(urlVpb);
					throw; // Rethrow the exception to be caught in the main method
				}
			}
		}
	}

	private static async Task DownloadAirport(string code, string version)
	{
		string subfolder = $"{version}/Airports/{string.Join("/", code[..^1].ToCharArray())}";
		string parent = $"{Util.TerrServerUrl}{subfolder}";
		try
		{
			HtmlDocument airportParentDir = new();
			airportParentDir.LoadHtml(System.Text.Encoding.UTF8.GetString(client.GetByteArrayAsync(parent).Result));
			foreach (HtmlNode node in airportParentDir.DocumentNode.SelectNodes("//tr")!)
			{
				if (!node.InnerHtml.Contains(".xml"))
					continue;

				HtmlDocument innerNode = new();
				innerNode.LoadHtml(node.OuterHtml); // For some reason, simply using the node in the foreach loop returns weird and incorrect results
				string airportAttribute = innerNode.DocumentNode.SelectSingleNode("//a")!.InnerHtml;
				if (airportAttribute.Split(".")[0] == code)
				{
					string urlApt = $"{parent}/{airportAttribute}";
					if (CurrentTasks.Add(urlApt))
					{
						Logger.Debug("DownloadMgr", $"Downloading {urlApt}");
						byte[] arptBytes = await client.GetByteArrayAsync(urlApt);
						string airportDir = Path.Combine(Util.SavePath, subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
						if (!Directory.Exists(airportDir))
						{
							_ = Directory.CreateDirectory(airportDir);
							Logger.Debug("DownloadMgr", "Creating airport directories...");
						}
						await File.WriteAllBytesAsync(Path.Combine(airportDir, airportAttribute), arptBytes);
						_ = CurrentTasks.Remove(urlApt);
					}
				}
			}
		}
		catch (HttpRequestException ex)
		{
			Logger.Error("DownloadMgr", "Error accessing airport data", ex);
		}
		;
	}

	private static async Task DownloadOrthophoto(double lat, double lon, int size)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = Util.GetTileIndex(lat, lon).ToString();
		string subfolder = $"{hemiLon}{(Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0')}{hemiLat}{(Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0')}/{hemiLon}{Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0')}{hemiLat}{Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0')}/";
		double[,] bbox = Util.GetTileBounds(lat, lon);
		double[,] bboxMercator = { { Math.Log(Math.Tan((90.0 + bbox[0, 0]) * Math.PI / 360.0)) * 6378137, bbox[0, 1] * Math.PI * 6378137 / 180 }, { Math.Log(Math.Tan((90.0 + bbox[1, 0]) * Math.PI / 360.0)) * 6378137, bbox[1, 1] * Math.PI * 6378137 / 180 } };
		string urlPic = $"https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox={bboxMercator[0, 1]}%2C{bboxMercator[0, 0]}%2C{bboxMercator[1, 1]}%2C{bboxMercator[1, 0]}&bboxSR=&layers=&layerDefs=&size={size}=%2C{size}&imageSR=&historicMoment=&format=jpg&transparent=false&dpi=&time=&timeRelation=esriTimeRelationOverlaps&layerTimeOptions=&dynamicLayers=&gdbVersion=&mapScale=&rotation=&datumTransformations=&layerParameterValues=&mapRangeValues=&layerRangeValues=&clipping=&spatialFilter=&f=image";
		try
		{
			if (CurrentTasks.Add(urlPic))
			{
				string orthophotoDir = Path.Combine(Util.SavePath, "Orthophotos", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar));
				if (!Directory.Exists(orthophotoDir))
				{
					_ = Directory.CreateDirectory(orthophotoDir);
					Logger.Debug("DownloadMgr", "Creating orthophoto directories...");
				}
				int subTileCount = size < 2048 ? 1 : (int)Math.Pow(size / 2048, 2);
				int tilesPerSide = (int)Math.Sqrt(subTileCount);
				double stepLat = Math.Abs((bboxMercator[1, 0] - bboxMercator[0, 0]) / tilesPerSide);
				double stepLon = Math.Abs((bboxMercator[1, 1] - bboxMercator[0, 1]) / tilesPerSide);
				double[] latSteps = [.. Enumerable.Range(0, tilesPerSide + 1).Select(k => bboxMercator[0, 0] + k * stepLat)];
				double[] lonSteps = [.. Enumerable.Range(0, tilesPerSide + 1).Select(k => bboxMercator[0, 1] + k * stepLon)];
				int count = 1;
				int totalHeight = 0;
				// These tiles need to be broken up into smaller tiles, because the ArcGIS server tends to give 504s higher than 2048px
				// Even if the tile is smaller than 2048px, this should still work fine
				for (int i = 0; i < latSteps.Length - 1; i++)
				{
					int curHeight = (int)Math.Abs(stepLat / stepLon * 2048);
					totalHeight += curHeight;
					for (int j = 0; j < lonSteps.Length - 1; j++)
					{
						string urlSubPic = $"https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox={lonSteps[j]}%2C{latSteps[i]}%2C{lonSteps[j + 1]}%2C{latSteps[i + 1]}&bboxSR=&layers=&layerDefs=&size=2048%2C{curHeight}&imageSR=&historicMoment=&format=jpg&transparent=false&dpi=&time=&timeRelation=esriTimeRelationOverlaps&layerTimeOptions=&dynamicLayers=&gdbVersion=&mapScale=&rotation=&datumTransformations=&layerParameterValues=&mapRangeValues=&layerRangeValues=&clipping=&spatialFilter=&f=image";
						Logger.Debug("DownloadMgr", $"Downloading {urlSubPic}");
						byte[] orthoSubBytes = await client.GetByteArrayAsync(urlSubPic);
						await File.WriteAllBytesAsync(Path.Combine(Util.TempPath, tile + (size > 2048 ? $"_{count}" : "") + ".jpg"), orthoSubBytes);
						count++;
					}
				}

				int rows = tilesPerSide;
				int cols = tilesPerSide;
				int tileWidth = 2048;
				int tileHeight = totalHeight / rows;
				Logger.Debug("DownloadMgr", $"{cols} columns, {rows} rows, {tileWidth}px width, {tileHeight}px height");
				if (totalHeight % rows != 0) tileHeight += totalHeight % rows;
				using SKBitmap stitched = new(2048 * tilesPerSide, tileHeight * tilesPerSide);
				// Stitch the tiles together
				using (SKCanvas canvas = new(stitched))
				{
					if (size > 2048)
					{
						int count2 = 1;
						for (int row = tilesPerSide - 1; row >= 0; row--)
						{
							for (int col = 0; col < tilesPerSide; col++)
							{
								string tilePath = Path.Combine(Util.TempPath, $"{tile}_{count2}.jpg");
								using SKBitmap subTile = SKBitmap.Decode(tilePath);
								canvas.DrawBitmap(subTile, new SKPoint(col * 2048, row * tileHeight));
								count2++;
								// Delete the sub-tile after stitching
								File.Delete(tilePath);
							}
						}
					}
					else
					{
						string tilePath = Path.Combine(Util.TempPath, $"{tile}.jpg");
						using SKBitmap subTile = SKBitmap.Decode(tilePath);
						canvas.DrawBitmap(subTile, new SKPoint(0, 0));
					}
					// Save the stitched image
					using SKImage image = SKImage.FromBitmap(stitched);

					// Resize the stitched image to be square (width x width)
					int squareSize = stitched.Width;
					using SKBitmap squareBitmap = new(squareSize, squareSize);
					using (SKCanvas squareCanvas = new(squareBitmap))
					{
						// Stretch the stitched image to fill the entire square bitmap
						var sourceRect = new SKRect(0, 0, stitched.Width, stitched.Height);
						var destRect = new SKRect(0, 0, squareSize, squareSize);
						squareCanvas.DrawBitmap(stitched, sourceRect, destRect);
					}
					using SKImage squareImage = SKImage.FromBitmap(squareBitmap);
					using SKData data = squareImage.Encode(SKEncodedImageFormat.Jpeg, 100);
					using FileStream stream = File.OpenWrite(Path.Combine(Util.TempPath, $"{tile}.jpg"));
					data.SaveTo(stream);
				}

				using Image<Rgba32> imageDDS = SixLabors.ImageSharp.Image.Load<Rgba32>(Path.Combine(Util.TempPath, $"{tile}.jpg"));
				BcEncoder encoder = new()
				{
					OutputOptions = {
						Format = CompressionFormat.Bc3,
						GenerateMipMaps = true,
						FileFormat = OutputFileFormat.Dds
					}
				};

				using FileStream fs = File.OpenWrite(Util.SavePath + "Orthophotos/" + subfolder + tile + ".dds");
				encoder.EncodeToStream(imageDDS, fs);

				File.Delete(Path.Combine(Util.TempPath, tile + ".jpg"));
				_ = CurrentTasks.Remove(urlPic);
			}
		}
		catch (HttpRequestException ex)
		{
			Logger.Debug("DownloadMgr", $"Error downloading image({urlPic}): {ex.Message}");
		}
	}

	private static async Task DownloadObjects(double lat, double lon)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = Util.GetTileIndex(lat, lon).ToString();
		string subfolder = $"{hemiLon}{(Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0')}{hemiLat}{(Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0')}/{hemiLon}{Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0')}{hemiLat}{Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0')}/";
		string urlObj = $"{Util.TerrServerUrl}ws2/Objects/{subfolder}{tile}.stg";

		if (CurrentTasks.Add(urlObj))
		{
			try
			{
				Logger.Debug("DownloadMgr", $"Downloading {urlObj}");
				byte[] objBytes = await client.GetByteArrayAsync(urlObj);
				string objectsDir = Path.Combine(Util.SavePath, "Objects", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar));
				if (!Directory.Exists(objectsDir))
				{
					_ = Directory.CreateDirectory(objectsDir);
					Logger.Debug("DownloadMgr", "Creating object directories...");
				}
				await File.WriteAllBytesAsync(Path.Combine(objectsDir, $"{tile}.stg"), objBytes);
				string[] objFile = System.Text.Encoding.UTF8.GetString(objBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
				foreach (string line in objFile)
				{
					string[] tokens = line.Split(' ');
					if (tokens[0] == "OBJECT_SHARED")
					{
						string acFile;
						string urlXml = $"{Util.TerrServerUrl}ws2/{tokens[1]}";
						if (CurrentTasks.Add(urlXml))
						{
							if (tokens[1].EndsWith(".xml"))
							{
								Logger.Debug("DownloadMgr", $"Downloading {urlXml}");
								byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
								await StorageHelper.WriteSceneryBytesAsync(tokens[1], objectBytes);
								_ = CurrentTasks.Remove(urlXml);
								string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
								acFile = $"{(Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/")}/{objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6)}";
							}
							else
							{
								acFile = tokens[1];
							}
							string urlAc = $"{Util.TerrServerUrl}ws2/{acFile}";
							if (CurrentTasks.Add(urlAc))
							{
								Logger.Debug("DownloadMgr", $"Downloading {urlAc}");
								byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
								await StorageHelper.WriteSceneryBytesAsync(acFile, modelBytes);
								_ = CurrentTasks.Remove(urlAc);
								string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
								foreach (string modelLine in modelFile)
								{
									if (modelLine.StartsWith("texture "))
									{
										string urlTex = $"{Util.TerrServerUrl}ws2/{(Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/")}/{modelLine[8..].Replace("\"", "")}";
										if (CurrentTasks.Add(urlTex))
										{
											Logger.Debug("DownloadMgr", $"Downloading {urlTex}");
											byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
											string texPath = Path.Combine((Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/"), modelLine[8..].Replace("\"", ""));
											await StorageHelper.WriteSceneryBytesAsync(texPath, textureBytes);
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
						string urlXml = $"{Util.TerrServerUrl}ws2/Objects/{subfolder}{tokens[1]}";
						if (CurrentTasks.Add(urlXml))
						{
							if (tokens[1].EndsWith(".xml"))
							{
								Logger.Debug("DownloadMgr", $"Downloading {urlXml}");
								byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
								try { await File.WriteAllBytesAsync(Path.Combine(Util.SavePath, "Objects", subfolder, tokens[1]), objectBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
								_ = CurrentTasks.Remove(urlXml);
								string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
								acFile = string.Concat((Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/"), objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6));
							}
							else
							{
								acFile = tokens[1];
							}
							string urlAc = $"{Util.TerrServerUrl}ws2/Objects/{subfolder}{acFile}";
							if (CurrentTasks.Add(urlAc))
							{
								Logger.Debug("DownloadMgr", $"Downloading {urlAc}");
								byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
								try { await File.WriteAllBytesAsync(Path.Combine(Util.SavePath, "Objects", subfolder, acFile), modelBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
								_ = CurrentTasks.Remove(urlAc);
								string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
								foreach (string modelLine in modelFile)
								{
									if (modelLine.StartsWith("texture "))
									{
										string urlTex = $"{Util.TerrServerUrl}ws2/Objects/{subfolder}{modelLine[8..].Replace("\"", "")}";
										if (CurrentTasks.Add(urlTex))
										{
											Logger.Debug("DownloadMgr", $"Downloading {urlTex}");
											byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
											try { await File.WriteAllBytesAsync(Path.Combine(Util.SavePath, "Objects", subfolder, modelLine[8..].Replace("\"", "")), textureBytes); } catch (IOException ex) { Logger.Warning("DownloadMgr", "Race condition writing file", ex); }
											_ = CurrentTasks.Remove(urlTex);
										}
									}
								}
							}
						}
					}
				}
			}
			catch (HttpRequestException)
			{
				_ = CurrentTasks.Remove(urlObj);
				throw; // Rethrow the exception to be caught in the main method
			}
			_ = CurrentTasks.Remove(urlObj);
		}
	}

	private static async Task DownloadOSM(double lat, double lon)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string subfolder = $"{hemiLon}{(Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0')}{hemiLat}{(Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0')}/{hemiLon}{Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0')}{hemiLat}{Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0')}";
		string subfolderTxz = $"{hemiLon}{(Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0')}{hemiLat}{(Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0')}/";
		string urlBuildings = $"{Util.TerrServerUrl}osm2city/Buildings/{subfolder}.txz";
		string urlDetails = $"{Util.TerrServerUrl}osm2city/Details/{subfolder}.txz";
		string urlPylons = $"{Util.TerrServerUrl}osm2city/Pylons/{subfolder}.txz";
		string urlRoads = $"{Util.TerrServerUrl}osm2city/Roads/{subfolder}.txz";
		string urlTrees = $"{Util.TerrServerUrl}osm2city/Trees/{subfolder}.txz";
		if (CurrentTasks.Add(urlBuildings))
		{
			try
			{
				Logger.Debug("DownloadMgr", $"Downloading {urlBuildings}");
				byte[] buildingsBytes = await client.GetByteArrayAsync(urlBuildings);
				string buildingsDir = Path.Combine(Util.SavePath, "Buildings", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
				if (!Directory.Exists(buildingsDir))
				{
					_ = Directory.CreateDirectory(buildingsDir);
					Logger.Debug("DownloadMgr", "Creating OSM building directories...");
				}
				string buildingsTxzPath = $"{Path.Combine(buildingsDir, "..")}.txz";
				await File.WriteAllBytesAsync(buildingsTxzPath, buildingsBytes);
				Logger.Info("DownloadMgr", $"Unzipping {buildingsTxzPath} ...");
				using FileStream xzStream = File.OpenRead(buildingsTxzPath);
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Path.Combine(Util.SavePath, "Buildings", subfolderTxz.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar), reader.Entry.ToString()));
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Logger.Debug("DownloadMgr", $"Buildings data not found for tile at {lat}, {lon}. Skipping buildings download.");
				}
				else
				{
					Logger.Debug("DownloadMgr", $"Error downloading file ({urlBuildings}): {ex.Message}");
				}
			}
			_ = CurrentTasks.Remove(urlBuildings);
		}
		if (CurrentTasks.Add(urlDetails))
		{
			try
			{
				Logger.Debug("DownloadMgr", $"Downloading {urlDetails}");
				byte[] detailsBytes = await client.GetByteArrayAsync(urlDetails);
				string detailsDir = Path.Combine(Util.SavePath, "Details", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
				if (!Directory.Exists(detailsDir))
				{
					_ = Directory.CreateDirectory(detailsDir);
					Logger.Debug("DownloadMgr", "Creating OSM detail directories...");
				}
				string detailsTxzPath = $"{Path.Combine(detailsDir, "..")}.txz";
				await File.WriteAllBytesAsync(detailsTxzPath, detailsBytes);
				Logger.Info("DownloadMgr", $"Unzipping {detailsTxzPath} ...");
				using FileStream xzStream = File.OpenRead(detailsTxzPath);
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Path.Combine(Util.SavePath, "Details", subfolderTxz.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar), reader.Entry.ToString()));
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Logger.Debug("DownloadMgr", $"Details data not found for tile at {lat}, {lon}. Skipping details download.");
				}
				else
				{
					Logger.Debug("DownloadMgr", $"Error downloading file ({urlDetails}): {ex.Message}");
				}
			}
			_ = CurrentTasks.Remove(urlDetails);
		}
		if (CurrentTasks.Add(urlPylons))
		{
			try
			{
				Logger.Debug("DownloadMgr", $"Downloading {urlPylons}");
				byte[] pylonsBytes = await client.GetByteArrayAsync(urlPylons);
				string pylonsDir = Path.Combine(Util.SavePath, "Pylons", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
				if (!Directory.Exists(pylonsDir))
				{
					_ = Directory.CreateDirectory(pylonsDir);
					Logger.Debug("DownloadMgr", "Creating OSM pylon directories...");
				}
				string pylonsTxzPath = $"{Path.Combine(pylonsDir, "..")}.txz";
				await File.WriteAllBytesAsync(pylonsTxzPath, pylonsBytes);
				Logger.Info("DownloadMgr", $"Unzipping {pylonsTxzPath} ...");
				using FileStream xzStream = File.OpenRead(pylonsTxzPath);
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Path.Combine(Util.SavePath, "Pylons", subfolderTxz.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar), reader.Entry.ToString()));
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Logger.Debug("DownloadMgr", $"Pylons data not found for tile at {lat}, {lon}. Skipping pylons download.");
				}
				else
				{
					Logger.Debug("DownloadMgr", $"Error downloading file ({urlPylons}): {ex.Message}");
				}
			}
			_ = CurrentTasks.Remove(urlPylons);
		}
		if (CurrentTasks.Add(urlRoads))
		{
			try
			{
				Logger.Debug("DownloadMgr", $"Downloading {urlRoads}");
				byte[] roadsBytes = await client.GetByteArrayAsync(urlRoads);
				string roadsDir = Path.Combine(Util.SavePath, "Roads", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
				if (!Directory.Exists(roadsDir))
				{
					_ = Directory.CreateDirectory(roadsDir);
					Logger.Debug("DownloadMgr", "Creating OSM road directories...");
				}
				string roadsTxzPath = $"{Path.Combine(roadsDir, "..")}.txz";
				await File.WriteAllBytesAsync(roadsTxzPath, roadsBytes);
				Logger.Info("DownloadMgr", $"Unzipping {roadsTxzPath} ...");
				using FileStream xzStream = File.OpenRead(roadsTxzPath);
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Path.Combine(Util.SavePath, "Roads", subfolderTxz.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar), reader.Entry.ToString()));
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Logger.Debug("DownloadMgr", $"Roads data not found for tile at {lat}, {lon}. Skipping roads download.");
				}
				else
				{
					Logger.Debug("DownloadMgr", $"Error downloading file ({urlRoads}): {ex.Message}");
				}
			}
			_ = CurrentTasks.Remove(urlRoads);
		}
		if (CurrentTasks.Add(urlTrees))
		{
			try
			{
				Logger.Debug("DownloadMgr", $"Downloading {urlTrees}");
				byte[] treesBytes = await client.GetByteArrayAsync(urlTrees);
				string treesDir = Path.Combine(Util.SavePath, "Trees", subfolder.Replace("/", Path.DirectorySeparatorChar.ToString()));
				if (!Directory.Exists(treesDir))
				{
					_ = Directory.CreateDirectory(treesDir);
					Logger.Debug("DownloadMgr", "Creating OSM tree directories...");
				}
				string treesTxzPath = $"{Path.Combine(treesDir, "..")}.txz";
				await File.WriteAllBytesAsync(treesTxzPath, treesBytes);
				Logger.Info("DownloadMgr", $"Unzipping {treesTxzPath} ...");
				using FileStream xzStream = File.OpenRead(treesTxzPath);
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Path.Combine(Util.SavePath, "Trees", subfolderTxz.Replace("/", Path.DirectorySeparatorChar.ToString()).TrimEnd(Path.DirectorySeparatorChar), reader.Entry.ToString()));
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Logger.Debug("DownloadMgr", $"Trees data not found for tile at {lat}, {lon}. Skipping trees download.");
				}
				else
				{
					Logger.Debug("DownloadMgr", $"Error downloading file ({urlTrees}): {ex.Message}");
				}
			}
			_ = CurrentTasks.Remove(urlTrees);
		}
	}

	public static async Task DownloadPlan(string filepath, int radius)
	{
		HashSet<(double, double)> tiles = [];
		if (filepath.EndsWith(".fgfp"))
		{
			XmlDocument plan = new();
			plan.Load(filepath);
			XmlNodeList waypoints = plan.GetElementsByTagName("wp");
			for (int i = 0; i < waypoints.Count - 1; i++)
			{
				XmlDocument currentWp = new();
				currentWp.LoadXml(waypoints[i]!.OuterXml);
				double wpCurLat = double.Parse(currentWp.SelectSingleNode("//lat")!.InnerText);
				double wpCurLon = double.Parse(currentWp.SelectSingleNode("//lon")!.InnerText);
				XmlDocument nextWp = new();
				nextWp.LoadXml(waypoints[i + 1]!.OuterXml);
				double wpNxtLat = double.Parse(nextWp.SelectSingleNode("//lat")!.InnerText);
				double wpNxtLon = double.Parse(nextWp.SelectSingleNode("//lon")!.InnerText);
				List<(double, double)> pointsAlongPath = GreatCircleInterpolator.GetGreatCirclePoints(wpCurLat, wpCurLon, wpNxtLat, wpNxtLon, (int)Math.Floor(Util.Haversine(wpCurLat, wpNxtLat, wpCurLon, wpNxtLon)) / 5);
				Logger.Debug("DownloadMgr", $"Points along path: {pointsAlongPath.Count}");
				_ = pointsAlongPath.Prepend((wpCurLat, wpCurLon));
				foreach ((double lat, double lon) in pointsAlongPath)
				{
					List<(double, double)> circle = Util.GetTilesWithinRadius(lat, lon, radius);
					foreach ((double latitude, double longitude) in circle)
					{
						_ = tiles.Add((latitude, longitude));
					}
				}
			}
		}
		Logger.Debug("DownloadMgr", $"{tiles.Count}");
		foreach ((double lat, double lon) in tiles)
		{
			_ = DownloadTile(lat, lon, Util.OrthoRes, "ws2");
		}
	}
}
