using System.Security.Cryptography;

namespace TerraMaster;

public struct TileRecord
{
	public string type; // terrain, object, ortho, buildings, details, pylons, roads, trees
	public bool latestVersion;
	public string url;
	public string path;
}

public struct UpdateCategory
{
	public string type; // no-update, update, updating, blacklist, selected
	public List<Polygon> polygons;
}

public struct Polygon
{
	public string type; // include, exclude
	public List<(int[] start, int[] end)> endpoints;
}

public class Index
{
	public static Dictionary<int, TileRecord> tiles = [];
	public static void IndexAllTiles()
	{
		// Let's make a directory to hold the files that we're getting version data from.
		// If we already have a file here, we shouldn't need to update it while the check is running.
		if (!Directory.Exists(Path.Combine(Util.TempPath, "sync")))
		{
			Directory.CreateDirectory(Path.Combine(Util.TempPath, "sync"));
		}

		// Look recursively through the scenery directory for terrain, object, ortho and OSM files/folders
		string terrainPath = Path.Combine(Util.SavePath, "ws2", "Terrain");
		string objectPath = Path.Combine(Util.SavePath, "Object");
		string orthoPath = Path.Combine(Util.SavePath, "Orthophotos");
		string buildingsPath = Path.Combine(Util.SavePath, "Buildings");
		string detailsPath = Path.Combine(Util.SavePath, "Details");
		string pylonsPath = Path.Combine(Util.SavePath, "Pylons");
		string roadsPath = Path.Combine(Util.SavePath, "Roads");
		string treesPath = Path.Combine(Util.SavePath, "Trees");

		foreach (string file in Directory.GetFiles(terrainPath, "*", SearchOption.AllDirectories))
		{
			if (file.EndsWith(".stg"))
			{
				string fileName = Path.GetFileName(file);
				string[] terrainLines = File.ReadAllLines(file);
				string url = file.Replace(Util.SavePath, Util.TerrServerUrl).Replace("\\", "/");
				bool isUpToDate = true;
				try
				{
					isUpToDate = IsTileUpToDate(url);
				}
				catch (Exception) {}

				foreach (string line in terrainLines) // Check all subrecords to make sure that they're up-to-date as well
				{
					string[] tokens = line.Split(' ');
					if (tokens[0] == "OBJECT" || tokens[0] == "OBJECT_BASE" || tokens[0] == "OBJECT_STATIC")
					{
						isUpToDate = isUpToDate && IsTileUpToDate(url.Replace(fileName, tokens[1]));
					}
					else if (tokens[0] == "OBJECT_SHARED")
					{
						isUpToDate = isUpToDate && IsTileUpToDate(Util.TerrServerUrl + "ws2/" + tokens[1]);
					}
				}
				tiles.Add(int.Parse(fileName.Split(".")[0]), new TileRecord
				{
					type = "terrain",
					latestVersion = isUpToDate,
					url = url,
					path = file
				});
			}
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

	public static bool IsTileUpToDate(string url)
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

	private static UpdateCategory GeneratePolygon(string type)
	{
		List<Polygon> polygons = [];
		List<TileRecord> categoryTiles;
		if (type == "update")
		{
			categoryTiles = tiles.Values.Where(t => t.type == type && !t.latestVersion).ToList();
		}
		return new UpdateCategory
		{
			type = type,
			polygons = polygons
		};
	}

	private static List<int> GetTileNeighbors(int tileId)
	{
		List<int> neighbors = [];
		neighbors.Add(tileId); // West
		var (lat, lon) = Util.GetLatLon(tileId);
		double[,] tileBounds = Util.GetTileBounds(lat, lon);
		int widthIntervals = (int)(tileBounds[0, 0] - tileBounds[0, 1] / 0.125);
		return neighbors;
	}
}