using System.Security.Cryptography;

namespace TerraMaster;

public class Index
{
	public static Dictionary<int, TileRecord> tiles = [];
	public static int QueuedTiles { get; private set; } = 0;
	public static int ProcessedTiles { get; private set; } = 0;

	public enum TileType
	{
		terrain,
		objects,
		ortho,
		buildings,
		details,
		pylons,
		roads,
		trees
	}

	public enum DisplayType
	{
		no_update,
		update,
		updating,
		blacklist,
		selected
	}

	public struct TileRecord
	{
		public TileType type; // terrain, object, ortho, buildings, details, pylons, roads, trees
		public bool latestVersion;
		public string url;
		public string path;
	}

	public struct DisplayCategory
	{
		public DisplayType type; // no-update, update, updating, blacklist, selected
		public List<Polygon> polygons;
	}

	public struct Polygon
	{
		public string type; // include, exclude
		public List<(int[] start, int[] end)> endpoints;
	}

	public static void IndexAllTiles(bool deep)
	{
		tiles.Clear();
		_ = IndexTerrain(deep);
		_ = IndexObjects(deep);
		// IndexOrtho(deep);
	}

	public static async Task IndexTerrain(bool deep)
	{
		if (!Directory.Exists(Path.Combine(Util.TempPath, "sync")))
		{
			Directory.CreateDirectory(Path.Combine(Util.TempPath, "sync"));
		}

		string[] stgFiles = [.. Directory.GetFiles(Path.Combine(Util.SavePath, "Terrain"), "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".stg"))];
		string[] btgFiles = [.. Directory.GetFiles(Path.Combine(Util.SavePath, "Terrain"), "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".btg.gz"))];
		QueuedTiles += deep ? stgFiles.Length + btgFiles.Length : stgFiles.Length;
		foreach (string file in deep ? stgFiles.Concat(btgFiles) : stgFiles)
		{
			string fileName = Path.GetFileName(file);
			string[] terrainLines = File.ReadAllLines(file);
			string url = file.Replace(Util.SavePath, Util.TerrServerUrl).Replace("\\", "/");
			bool isUpToDate = true;
			try
			{
				isUpToDate = IsUrlUpToDate(url);
			}
			catch (Exception) { }

			if (deep)
			{
				foreach (string line in terrainLines) // Check all subrecords to make sure that they're up-to-date as well
				{
					string[] tokens = line.Split(' ');
					if (tokens[0] == "OBJECT" || tokens[0] == "OBJECT_BASE" || tokens[0] == "OBJECT_STATIC")
					{
						isUpToDate = isUpToDate && IsUrlUpToDate(url.Replace(fileName, tokens[1]));
					}
					else if (tokens[0] == "OBJECT_SHARED")
					{
						isUpToDate = isUpToDate && IsUrlUpToDate(Util.TerrServerUrl + "ws2/" + tokens[1]);
					}
				}
			}
			tiles.Add(int.Parse(fileName.Split(".")[0]), new TileRecord
			{
				type = TileType.terrain,
				latestVersion = isUpToDate,
				url = url,
				path = file
			});
			ProcessedTiles++;
		}
	}

	public static async Task IndexObjects(bool deep)
	{
		if (!Directory.Exists(Path.Combine(Util.TempPath, "sync")))
		{
			Directory.CreateDirectory(Path.Combine(Util.TempPath, "sync"));
		}

		string[] stgFiles = [.. Directory.GetFiles(Path.Combine(Util.SavePath, "Objects"), "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".stg"))];
		QueuedTiles += stgFiles.Length;
		foreach (string file in stgFiles)
		{
			string fileName = Path.GetFileName(file);
			string[] objectLines = File.ReadAllLines(file);
			string url = (Util.TerrServerUrl + "ws2/" + file.Replace(Util.SavePath, "")).Replace("\\", "/");
			Console.WriteLine(url);
			bool isUpToDate = true;
			try
			{
				isUpToDate = IsUrlUpToDate(url);
			}
			catch (Exception) { }

			if (deep)
			{
				foreach (string line in objectLines) // Check all subrecords to make sure that they're up-to-date as well
				{
					string[] tokens = line.Split(' ');
					if (tokens[0] == "OBJECT" || tokens[0] == "OBJECT_BASE" || tokens[0] == "OBJECT_STATIC")
					{
						isUpToDate = isUpToDate && IsUrlUpToDate(url.Replace(fileName, tokens[1]));
					}
					else if (tokens[0] == "OBJECT_SHARED")
					{
						isUpToDate = isUpToDate && IsUrlUpToDate(Util.TerrServerUrl + "ws2/" + tokens[1]);
					}
				}
			}
			tiles.Add(int.Parse(fileName.Split(".")[0]), new TileRecord
			{
				type = TileType.objects,
				latestVersion = isUpToDate,
				url = url,
				path = file
			});
			ProcessedTiles++;
		}
	}

	private static Dictionary<string, string> GetDirindex(string path)
	{
		Dictionary<string, string> dirindex = [];
		string[] dirindexLines = File.ReadAllLines(path);
		foreach (string line in dirindexLines)
		{
			string[] tokens = line.Split(":");
			if (tokens.Length == 4)
				dirindex.Add(tokens[1], tokens[2]);
		}
		return dirindex;
	}

	public static bool IsUrlUpToDate(string url)
	{
		string fileName = url.Replace(Util.TerrServerUrl, Util.SavePath).Replace("/", "\\");
		if (!File.Exists(fileName))
			return false;
		string dirindexPath = Path.Combine(Util.TempPath, "sync", Path.GetDirectoryName(fileName).Replace(Util.SavePath, "").Replace("\\", "_") + ".dirindex");
		if (!File.Exists(Util.TempPath + "/sync/" + fileName.Replace(Util.SavePath, "").Replace("\\", "_") + ".dirindex"))
		{
			// Download the dirindex file to the temp folder
			string dirindexUrl = url.Replace(fileName, ".dirindex");
			try
			{
				byte[] dirindexBytes = DownloadMgr.client.GetByteArrayAsync(dirindexUrl).Result;
				File.WriteAllBytes(dirindexPath, dirindexBytes);
			}
			catch (Exception)
			{
				return false;
			}
		}
		Dictionary<string, string> dirindex = GetDirindex(dirindexPath);
		FileStream stream = File.OpenRead(fileName);
		byte[] hash = SHA1.Create().ComputeHash(stream);
		stream.Close();
		string sha1Hash = Convert.ToHexStringLower(hash);
		if (dirindex.TryGetValue(fileName, out string? value))
		{
			return value == sha1Hash;
		}
		else
		{
			return true; // File not listed in dirindex, assume up-to-date
		}
	}

	private static DisplayCategory GeneratePolygon(DisplayType type)
	{
		List<Polygon> polygons = [];
		List<TileRecord> categoryTiles;
		if (type == DisplayType.update)
		{
			categoryTiles = [.. tiles.Values.Where(t => !t.latestVersion)];
		}
		else if (type == DisplayType.no_update)
		{
			categoryTiles = [.. tiles.Values.Where(t => t.latestVersion)];
		}
		else if (type == DisplayType.updating)
		{
			categoryTiles = [.. tiles.Values.Where(t => t.latestVersion == false /* && DownloadMgr.activeDownloads.ContainsKey(t.url) */)];
		}
		else if (type == DisplayType.blacklist)
		{
			categoryTiles = [.. tiles.Values.Where(t => t.type == TileType.terrain && !t.latestVersion)]; // Placeholder for blacklist
		}
		else if (type == DisplayType.selected)
		{
			categoryTiles = [.. tiles.Values.Where(t => t.type == TileType.terrain && !t.latestVersion)]; // Placeholder for selected
		}
		return new DisplayCategory
		{
			type = type,
			polygons = polygons
		};
	}

	private static HashSet<int> GetTileNeighbors(int tileId, HashSet<int> checkedTiles)
	{
		HashSet<int> neighbors = [];
		neighbors.Add(tileId); // West
		var (lat, lon) = Util.GetLatLon(tileId);
		double[,] tileBounds = Util.GetTileBounds(lat, lon);
		int widthIntervals = (int)((tileBounds[0, 0] - tileBounds[0, 1]) / 0.125);
		HashSet<int> nextTilesToCheck = [];
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= widthIntervals; j++)
			{
				double checkLat = lat + (i * 0.125);
				double checkLon = lon + (j * 0.125);
				if (Math.Abs(checkLat) <= 90 && Math.Abs(checkLon) <= 180 && !checkedTiles.Contains(Util.GetTileIndex(checkLat, checkLon)))
					continue;
				nextTilesToCheck.Add(Util.GetTileIndex(checkLat, checkLon));
			}
			lon += 0.125;
			nextTilesToCheck.Add(Util.GetTileIndex(lat, lon));
		}
		return neighbors;
	}
}
