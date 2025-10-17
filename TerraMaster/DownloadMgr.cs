using System.Xml;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using HtmlAgilityPack;
using SharpCompress.Readers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace TerraMaster;

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
					Console.WriteLine($"Terrain data not found for tile at {lat}, {lon}. Skipping terrain download.");
					_ = taskQueue.Release();
					return;
				}
				else
				{
					Console.WriteLine($"Error downloading terrain data: {ex.Message}");
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
					Console.WriteLine($"Object data not found for tile at {lat}, {lon}. Skipping object download.");
					_ = taskQueue.Release();
					return;
				}
				else
				{
					Console.WriteLine($"Error downloading object data: {ex.Message}");
				}
			}
			await DownloadOSM(lat, lon);
			_ = taskQueue.Release();

			Console.WriteLine($"Download successful.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error downloading tile: {ex.Message}");
		}
		_ = taskQueue.Release();
	}

	private static async Task DownloadTerrain(double lat, double lon, string version)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = Util.GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		string urlStg = Util.TerrServerUrl + version + "/Terrain/" + subfolder + tile + ".stg";

		if (version == "ws2")
		{
			string urlBtg = Util.TerrServerUrl + version + "/Terrain/" + subfolder + tile + ".btg.gz"; // Only used in WS2
			string Ws2Dir = Util.SavePath + "ws2/";
			if (CurrentTasks.Add(urlBtg))
			{
				try
				{
					Console.WriteLine("Downloading " + urlBtg + " ...");
					byte[] btgBytes = await client.GetByteArrayAsync(urlBtg);
					if (!Directory.Exists(Ws2Dir + "Terrain/" + subfolder))
					{
						_ = Directory.CreateDirectory(Ws2Dir + "Terrain/" + subfolder);
						Console.WriteLine("Creating terrain directories...");
					}
					await File.WriteAllBytesAsync(Ws2Dir + "Terrain/" + subfolder + tile + ".btg.gz", btgBytes);
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
					Console.WriteLine("Downloading " + urlStg + " ...");
					byte[] stgBytes = await client.GetByteArrayAsync(urlStg);
					if (!Directory.Exists(Ws2Dir + "Terrain/" + subfolder))
					{
						_ = Directory.CreateDirectory(Ws2Dir + "Terrain/" + subfolder);
						Console.WriteLine("Creating terrain directories...");
					}
					Console.WriteLine(Ws2Dir + "Terrain/" + subfolder + tile + ".stg");
					await File.WriteAllBytesAsync(Ws2Dir + "Terrain/" + subfolder + tile + ".stg", stgBytes);
					string[] stgFile = System.Text.Encoding.UTF8.GetString(stgBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
					foreach (string line in stgFile)
					{
						string[] tokens = line.Split(' ');
						if (tokens[0] == "OBJECT")
						{
							_ = DownloadAirport(tokens[1].Split(".")[0], version);
							string urlObj = Util.TerrServerUrl + version + "/Terrain/" + subfolder + tokens[1] + ".gz";
							if (CurrentTasks.Add(urlObj))
							{
								Console.WriteLine("Downloading " + urlObj + " ...");
								byte[] objectBytes = await client.GetByteArrayAsync(urlObj);
								try { await File.WriteAllBytesAsync(Ws2Dir + "/Terrain/" + subfolder + tokens[1] + ".gz", objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlObj);
							}
						}
						else if (tokens[0] == "OBJECT_SHARED")
						{
							if (!Directory.Exists(Path.GetDirectoryName(Util.SavePath + "/" + tokens[1])))
							{
								_ = Directory.CreateDirectory(Path.GetDirectoryName(Util.SavePath + "/" + tokens[1]) ?? "");
							}

							string acFile;
							string urlXml = Util.TerrServerUrl + version + "/" + tokens[1];
							if (CurrentTasks.Add(urlXml))
							{
								if (tokens[1].EndsWith(".xml"))
								{
									Console.WriteLine("Downloading " + urlXml + " ...");
									byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
									try { await File.WriteAllBytesAsync(Util.SavePath + "/" + tokens[1], objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
									_ = CurrentTasks.Remove(urlXml);
									string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
									acFile = string.Concat((Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/"), "/", objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6));
								}
								else
								{
									acFile = tokens[1];
								}
								string urlAc = Util.TerrServerUrl + version + "/" + acFile;
								if (CurrentTasks.Add(urlAc))
								{
									Console.WriteLine("Downloading " + urlAc + " ...");
									byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
									try { await File.WriteAllBytesAsync(Util.SavePath + "/" + acFile, modelBytes); } catch (IOException) { Console.WriteLine("RACE"); }
									_ = CurrentTasks.Remove(urlAc);
									string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
									foreach (string modelLine in modelFile)
									{
										if (modelLine.StartsWith("texture "))
										{
											string urlTex = Util.TerrServerUrl + version + "/" + (Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", "");
											if (CurrentTasks.Add(urlTex))
											{
												Console.WriteLine("Downloading " + urlTex + " ...");
												byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
												try { await File.WriteAllBytesAsync(Util.SavePath + "/" + (Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { Console.WriteLine("RACE"); }
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
			string urlVpb = Util.TerrServerUrl + version + "/vpb/" + subfolder[..^1] + ".zip"; // Only used in WS3
			string Ws3Dir = Util.SavePath + "ws3/";
			if (CurrentTasks.Add(urlStg))
			{
				try
				{
					Console.WriteLine("Downloading " + urlStg + " ...");
					byte[] stgBytes = await client.GetByteArrayAsync(urlStg);
					if (!Directory.Exists(Ws3Dir + "Terrain/" + subfolder))
					{
						_ = Directory.CreateDirectory(Ws3Dir + "Terrain/" + subfolder);
						Console.WriteLine("Creating terrain directories...");
					}
					Console.WriteLine(Ws3Dir + "Terrain/" + subfolder + tile + ".stg");
					await File.WriteAllBytesAsync(Ws3Dir + "Terrain/" + subfolder + tile + ".stg", stgBytes);
					string[] stgFile = System.Text.Encoding.UTF8.GetString(stgBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
					foreach (string line in stgFile)
					{
						string[] tokens = line.Split(' ');
						if (tokens[0] == "OBJECT")
						{
							_ = DownloadAirport(tokens[1].Split(".")[0], version);
							string urlObj = Util.TerrServerUrl + version + "/Terrain/" + subfolder + tokens[1] + ".gz";
							if (CurrentTasks.Add(urlObj))
							{
								Console.WriteLine("Downloading " + urlObj + " ...");
								byte[] objectBytes = await client.GetByteArrayAsync(urlObj);
								try { await File.WriteAllBytesAsync(Ws3Dir + "/Terrain/" + subfolder + tokens[1] + ".gz", objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlObj);
							}
						}
						else if (tokens[0] == "OBJECT_SHARED")
						{
							if (!Directory.Exists(Path.GetDirectoryName(Util.SavePath + "/" + tokens[1])))
							{
								_ = Directory.CreateDirectory(Path.GetDirectoryName(Util.SavePath + "/" + tokens[1]) ?? "");
							}

							string acFile;
							string urlXml = Util.TerrServerUrl + version + "/" + tokens[1];
							if (CurrentTasks.Add(urlXml))
							{
								if (tokens[1].EndsWith(".xml"))
								{
									Console.WriteLine("Downloading " + urlXml + " ...");
									byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
									try { await File.WriteAllBytesAsync(Util.SavePath + "/" + tokens[1], objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
									_ = CurrentTasks.Remove(urlXml);
									string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
									acFile = string.Concat((Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/"), "/", objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6));
								}
								else
								{
									acFile = tokens[1];
								}
								string urlAc = Util.TerrServerUrl + version + "/" + acFile;
								if (CurrentTasks.Add(urlAc))
								{
									Console.WriteLine("Downloading " + urlAc + " ...");
									byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
									try { await File.WriteAllBytesAsync(Util.SavePath + "/" + acFile, modelBytes); } catch (IOException) { Console.WriteLine("RACE"); }
									_ = CurrentTasks.Remove(urlAc);
									string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
									foreach (string modelLine in modelFile)
									{
										if (modelLine.StartsWith("texture "))
										{
											string urlTex = Util.TerrServerUrl + version + "/" + (Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", "");
											if (CurrentTasks.Add(urlTex))
											{
												Console.WriteLine("Downloading " + urlTex + " ...");
												byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
												try { await File.WriteAllBytesAsync(Util.SavePath + "/" + (Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { Console.WriteLine("RACE"); }
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
					Console.WriteLine("Downloading " + urlVpb + " ...");
					byte[] vpbBytes = await client.GetByteArrayAsync(urlVpb);
					await File.WriteAllBytesAsync(Path.Combine(Util.TempPath, subfolder[..^1].Split("/")[1] + ".zip"), vpbBytes);
					using FileStream zipStream = File.OpenRead(Path.Combine(Util.TempPath, subfolder[..^1].Split("/")[1] + ".zip"));
					using var reader = ReaderFactory.Open(zipStream);
					while (reader.MoveToNextEntry())
					{
						if (!reader.Entry.IsDirectory)
						{
							string entryString = reader.Entry.ToString() ?? "";
							string vpbDir;
							if (entryString.Length == 15 || entryString.EndsWith("added"))
							{
								vpbDir = Ws3Dir + "vpb/" + subfolder + "/";
							}
							else
							{
								vpbDir = Ws3Dir + "vpb/" + subfolder + entryString[..10] + "_root_L0_X0_Y0/";
							}
							if (!Directory.Exists(vpbDir))
							{
								_ = Directory.CreateDirectory(vpbDir);
							}
							using FileStream zipFileStream = File.Create(vpbDir + entryString);
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
		string subfolder = version + "/Airports/" + string.Join("/", code[..^1].ToCharArray());
		string parent = Util.TerrServerUrl + subfolder;
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
						if (!Directory.Exists(Util.SavePath + subfolder))
						{
							_ = Directory.CreateDirectory(Util.SavePath + subfolder);
							Console.WriteLine("Creating airport directories...");
						}
						await File.WriteAllBytesAsync(Util.SavePath + subfolder + "/" + airportAttribute, arptBytes);
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

	private static async Task DownloadOrthophoto(double lat, double lon, int size)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = Util.GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		double[,] bbox = Util.GetTileBounds(lat, lon);
		double[,] bboxMercator = { { Math.Log(Math.Tan((90.0 + bbox[0, 0]) * Math.PI / 360.0)) * 6378137, bbox[0, 1] * Math.PI * 6378137 / 180 }, { Math.Log(Math.Tan((90.0 + bbox[1, 0]) * Math.PI / 360.0)) * 6378137, bbox[1, 1] * Math.PI * 6378137 / 180 } };
		string urlPic = $"https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox={bboxMercator[0, 1]}%2C{bboxMercator[0, 0]}%2C{bboxMercator[1, 1]}%2C{bboxMercator[1, 0]}&bboxSR=&layers=&layerDefs=&size={size}=%2C{size}&imageSR=&historicMoment=&format=jpg&transparent=false&dpi=&time=&timeRelation=esriTimeRelationOverlaps&layerTimeOptions=&dynamicLayers=&gdbVersion=&mapScale=&rotation=&datumTransformations=&layerParameterValues=&mapRangeValues=&layerRangeValues=&clipping=&spatialFilter=&f=image";
		try
		{
			if (CurrentTasks.Add(urlPic))
			{
				if (!Directory.Exists(Util.SavePath + "Orthophotos/" + subfolder))
				{
					_ = Directory.CreateDirectory(Util.SavePath + "Orthophotos/" + subfolder);
					Console.WriteLine("Creating orthophoto directories...");
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
						Console.WriteLine("Downloading " + urlSubPic + " ...");
						byte[] orthoSubBytes = await client.GetByteArrayAsync(urlSubPic);
						await File.WriteAllBytesAsync(Path.Combine(Util.TempPath, tile + (size > 2048 ? "_" + count : "") + ".jpg"), orthoSubBytes);
						count++;
					}
				}

				int rows = tilesPerSide;
				int cols = tilesPerSide;
				int tileWidth = 2048;
				int tileHeight = totalHeight / rows;
				Console.WriteLine(cols + " columns, " + rows + " rows, " + tileWidth + "px width, " + tileHeight + "px height");
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
								string tilePath = Path.Combine(Util.TempPath, tile + "_" + count2 + ".jpg");
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
						string tilePath = Path.Combine(Util.TempPath, tile + ".jpg");
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
					using FileStream stream = File.OpenWrite(Path.Combine(Util.TempPath, tile + ".jpg"));
					data.SaveTo(stream);
				}

				using Image<Rgba32> imageDDS = SixLabors.ImageSharp.Image.Load<Rgba32>(Path.Combine(Util.TempPath, tile + ".jpg"));

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
			Console.WriteLine("Error downloading image(" + urlPic + "): " + ex.Message);
		}
	}

	private static async Task DownloadObjects(double lat, double lon)
	{
		string hemiLat = lat > 0 ? "n" : "s";
		string hemiLon = lon > 0 ? "e" : "w";
		string tile = Util.GetTileIndex(lat, lon).ToString();
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0') + "/";
		string urlObj = Util.TerrServerUrl + "ws2/Objects/" + subfolder + tile + ".stg";

		if (CurrentTasks.Add(urlObj))
		{
			try
			{
				Console.WriteLine("Downloading " + urlObj + " ...");
				byte[] objBytes = await client.GetByteArrayAsync(urlObj);
				if (!Directory.Exists(Util.SavePath + "Objects/" + subfolder))
				{
					_ = Directory.CreateDirectory(Util.SavePath + "Objects/" + subfolder);
					Console.WriteLine("Creating object directories...");
				}
				await File.WriteAllBytesAsync(Util.SavePath + "Objects/" + subfolder + tile + ".stg", objBytes);
				string[] objFile = System.Text.Encoding.UTF8.GetString(objBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
				foreach (string line in objFile)
				{
					string[] tokens = line.Split(' ');
					if (tokens[0] == "OBJECT_SHARED")
					{
						if (!Directory.Exists(Path.GetDirectoryName(Util.SavePath + "/" + tokens[1])))
						{
							_ = Directory.CreateDirectory(Path.GetDirectoryName(Util.SavePath + "/" + tokens[1]) ?? "");
						}

						string acFile;
						string urlXml = Util.TerrServerUrl + "ws2/" + tokens[1];
						if (CurrentTasks.Add(urlXml))
						{
							if (tokens[1].EndsWith(".xml"))
							{
								Console.WriteLine("Downloading " + urlXml + " ...");
								byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
								try { await File.WriteAllBytesAsync(Util.SavePath + "/" + tokens[1], objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlXml);
								string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
								acFile = string.Concat((Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/"), "/", objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6));
							}
							else
							{
								acFile = tokens[1];
							}
							string urlAc = Util.TerrServerUrl + "ws2/" + acFile;
							if (CurrentTasks.Add(urlAc))
							{
								Console.WriteLine("Downloading " + urlAc + " ...");
								byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
								try { await File.WriteAllBytesAsync(Util.SavePath + "/" + acFile, modelBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlAc);
								string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
								foreach (string modelLine in modelFile)
								{
									if (modelLine.StartsWith("texture "))
									{
										string urlTex = Util.TerrServerUrl + "ws2/" + (Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", "");
										if (CurrentTasks.Add(urlTex))
										{
											Console.WriteLine("Downloading " + urlTex + " ...");
											byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
											try { await File.WriteAllBytesAsync(Util.SavePath + "/" + (Path.GetDirectoryName(acFile) ?? "").Replace("\\", "/") + "/" + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { Console.WriteLine("RACE"); }
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
						string urlXml = Util.TerrServerUrl + "ws2/Objects/" + subfolder + tokens[1];
						if (CurrentTasks.Add(urlXml))
						{
							if (tokens[1].EndsWith(".xml"))
							{
								Console.WriteLine("Downloading " + urlXml + " ...");
								byte[] objectBytes = await client.GetByteArrayAsync(urlXml);
								try { await File.WriteAllBytesAsync(Util.SavePath + "/Objects/" + subfolder + tokens[1], objectBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlXml);
								string objectFile = System.Text.Encoding.UTF8.GetString(objectBytes);
								acFile = string.Concat((Path.GetDirectoryName(tokens[1]) ?? "").Replace("\\", "/"), objectFile.AsSpan(objectFile.IndexOf("<path>") + 6, objectFile.IndexOf("</path>") - objectFile.IndexOf("<path>") - 6));
							}
							else
							{
								acFile = tokens[1];
							}
							string urlAc = Util.TerrServerUrl + "ws2/Objects/" + subfolder + acFile;
							if (CurrentTasks.Add(urlAc))
							{
								Console.WriteLine("Downloading " + urlAc + " ...");
								byte[] modelBytes = await client.GetByteArrayAsync(urlAc);
								try { await File.WriteAllBytesAsync(Util.SavePath + "/Objects/" + subfolder + acFile, modelBytes); } catch (IOException) { Console.WriteLine("RACE"); }
								_ = CurrentTasks.Remove(urlAc);
								string[] modelFile = System.Text.Encoding.UTF8.GetString(modelBytes).Split(["\r\n", "\n"], StringSplitOptions.None);
								foreach (string modelLine in modelFile)
								{
									if (modelLine.StartsWith("texture "))
									{
										string urlTex = Util.TerrServerUrl + "ws2/Objects/" + subfolder + modelLine[8..].Replace("\"", "");
										if (CurrentTasks.Add(urlTex))
										{
											Console.WriteLine("Downloading " + urlTex + " ...");
											byte[] textureBytes = await client.GetByteArrayAsync(urlTex);
											try { await File.WriteAllBytesAsync(Util.SavePath + "Objects/" + subfolder + modelLine[8..].Replace("\"", ""), textureBytes); } catch (IOException) { Console.WriteLine("RACE"); }
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
		string subfolder = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/" + hemiLon + Math.Abs(Math.Floor(lon)).ToString().PadLeft(3, '0') + hemiLat + Math.Abs(Math.Floor(lat)).ToString().PadLeft(2, '0');
		string subfolderTxz = hemiLon + (Math.Abs(Math.Floor(lon / 10)) * 10).ToString().PadLeft(3, '0') + hemiLat + (Math.Abs(Math.Floor(lat / 10)) * 10).ToString().PadLeft(2, '0') + "/";
		string urlBuildings = Util.TerrServerUrl + "osm2city/Buildings/" + subfolder + ".txz";
		string urlDetails = Util.TerrServerUrl + "osm2city/Details/" + subfolder + ".txz";
		string urlPylons = Util.TerrServerUrl + "osm2city/Pylons/" + subfolder + ".txz";
		string urlRoads = Util.TerrServerUrl + "osm2city/Roads/" + subfolder + ".txz";
		string urlTrees = Util.TerrServerUrl + "osm2city/Trees/" + subfolder + ".txz";
		if (CurrentTasks.Add(urlBuildings))
		{
			try
			{
				Console.WriteLine("Downloading " + urlBuildings + " ...");
				byte[] buildingsBytes = await client.GetByteArrayAsync(urlBuildings);
				if (!Directory.Exists(Util.SavePath + "Buildings/" + subfolder))
				{
					_ = Directory.CreateDirectory(Util.SavePath + "Buildings/" + subfolder);
					Console.WriteLine("Creating OSM building directories...");
				}
				await File.WriteAllBytesAsync(Util.SavePath + "Buildings/" + subfolder + ".txz", buildingsBytes);
				Console.WriteLine("Unzipping " + Util.SavePath + "Buildings/" + subfolder + ".txz ...");
				using FileStream xzStream = File.OpenRead(Util.SavePath + "Buildings/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Util.SavePath + "Buildings/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Console.WriteLine($"Buildings data not found for tile at {lat}, {lon}. Skipping buildings download.");
				}
				else
				{
					Console.WriteLine("Error downloading file (" + urlBuildings + "): " + ex.Message);
				}
			}
			_ = CurrentTasks.Remove(urlBuildings);
		}
		if (CurrentTasks.Add(urlDetails))
		{
			try
			{
				Console.WriteLine("Downloading " + urlDetails + " ...");
				byte[] detailsBytes = await client.GetByteArrayAsync(urlDetails);
				if (!Directory.Exists(Util.SavePath + "Details/" + subfolder))
				{
					_ = Directory.CreateDirectory(Util.SavePath + "Details/" + subfolder);
					Console.WriteLine("Creating OSM detail directories...");
				}
				await File.WriteAllBytesAsync(Util.SavePath + "Details/" + subfolder + ".txz", detailsBytes);
				Console.WriteLine("Unzipping " + Util.SavePath + "Details/" + subfolder + ".txz ...");
				using FileStream xzStream = File.OpenRead(Util.SavePath + "Details/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Util.SavePath + "Details/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Console.WriteLine($"Details data not found for tile at {lat}, {lon}. Skipping details download.");
				}
				else
				{
					Console.WriteLine("Error downloading file (" + urlDetails + "): " + ex.Message);
				}
			}
			_ = CurrentTasks.Remove(urlDetails);
		}
		if (CurrentTasks.Add(urlPylons))
		{
			try
			{
				Console.WriteLine("Downloading " + urlPylons + " ...");
				byte[] pylonsBytes = await client.GetByteArrayAsync(urlPylons);
				if (!Directory.Exists(Util.SavePath + "Pylons/" + subfolder))
				{
					_ = Directory.CreateDirectory(Util.SavePath + "Pylons/" + subfolder);
					Console.WriteLine("Creating OSM pylon directories...");
				}
				await File.WriteAllBytesAsync(Util.SavePath + "Pylons/" + subfolder + ".txz", pylonsBytes);
				Console.WriteLine("Unzipping " + Util.SavePath + "Pylons/" + subfolder + ".txz ...");
				using FileStream xzStream = File.OpenRead(Util.SavePath + "Pylons/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Util.SavePath + "Pylons/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Console.WriteLine($"Pylons data not found for tile at {lat}, {lon}. Skipping pylons download.");
				}
				else
				{
					Console.WriteLine("Error downloading file (" + urlPylons + "): " + ex.Message);
				}
			}
			_ = CurrentTasks.Remove(urlPylons);
		}
		if (CurrentTasks.Add(urlRoads))
		{
			try
			{
				Console.WriteLine("Downloading " + urlRoads + " ...");
				byte[] roadsBytes = await client.GetByteArrayAsync(urlRoads);
				if (!Directory.Exists(Util.SavePath + "Roads/" + subfolder))
				{
					_ = Directory.CreateDirectory(Util.SavePath + "Roads/" + subfolder);
					Console.WriteLine("Creating OSM road directories...");
				}
				await File.WriteAllBytesAsync(Util.SavePath + "Roads/" + subfolder + ".txz", roadsBytes);
				Console.WriteLine("Unzipping " + Util.SavePath + "Roads/" + subfolder + ".txz ...");
				using FileStream xzStream = File.OpenRead(Util.SavePath + "Roads/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Util.SavePath + "Roads/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Console.WriteLine($"Roads data not found for tile at {lat}, {lon}. Skipping roads download.");
				}
				else
				{
					Console.WriteLine("Error downloading file (" + urlRoads + "): " + ex.Message);
				}
			}
			_ = CurrentTasks.Remove(urlRoads);
		}
		if (CurrentTasks.Add(urlTrees))
		{
			try
			{
				Console.WriteLine("Downloading " + urlTrees + " ...");
				byte[] treesBytes = await client.GetByteArrayAsync(urlTrees);
				if (!Directory.Exists(Util.SavePath + "Trees/" + subfolder))
				{
					_ = Directory.CreateDirectory(Util.SavePath + "Trees/" + subfolder);
					Console.WriteLine("Creating OSM tree directories...");
				}
				await File.WriteAllBytesAsync(Util.SavePath + "Trees/" + subfolder + ".txz", treesBytes);
				Console.WriteLine("Unzipping " + Util.SavePath + "Trees/" + subfolder + ".txz ...");
				using FileStream xzStream = File.OpenRead(Util.SavePath + "Trees/" + subfolder + ".txz");
				using var reader = ReaderFactory.Open(xzStream);
				while (reader.MoveToNextEntry())
				{
					if (!reader.Entry.IsDirectory)
					{
						using FileStream tarFileStream = File.Create(Util.SavePath + "Trees/" + subfolderTxz + reader.Entry);
						reader.WriteEntryTo(tarFileStream);
					}
				}
			}
			catch (HttpRequestException ex)
			{
				if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					Console.WriteLine($"Trees data not found for tile at {lat}, {lon}. Skipping trees download.");
				}
				else
				{
					Console.WriteLine("Error downloading file (" + urlTrees + "): " + ex.Message);
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
				currentWp.LoadXml(waypoints[i].OuterXml);
				double wpCurLat = double.Parse(currentWp.SelectSingleNode("//lat").InnerText);
				double wpCurLon = double.Parse(currentWp.SelectSingleNode("//lon").InnerText);
				XmlDocument nextWp = new();
				nextWp.LoadXml(waypoints[i + 1].OuterXml);
				double wpNxtLat = double.Parse(nextWp.SelectSingleNode("//lat").InnerText);
				double wpNxtLon = double.Parse(nextWp.SelectSingleNode("//lon").InnerText);
				List<(double, double)> pointsAlongPath = GreatCircleInterpolator.GetGreatCirclePoints(wpCurLat, wpCurLon, wpNxtLat, wpNxtLon, (int)Math.Floor(Util.Haversine(wpCurLat, wpNxtLat, wpCurLon, wpNxtLon)) / 5);
				Console.WriteLine("Points along path: " + pointsAlongPath.Count);
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
		Console.WriteLine(tiles.Count);
		foreach ((double lat, double lon) in tiles)
		{
			_ = DownloadTile(lat, lon, Util.OrthoRes, "ws2");
		}
	}
}
