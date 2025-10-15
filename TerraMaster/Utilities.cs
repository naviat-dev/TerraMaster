namespace TerraMaster;

public class Util
{
	public static readonly double[,] LatitudeIndex = { { 89, 12 }, { 86, 4 }, { 83, 2 }, { 76, 1 }, { 62, 0.5 }, { 22, 0.25 }, { 0, 0.125 } };
	public static readonly string SavePath = "C:\\Users\\User\\Documents\\Aviation\\scenery-test\\";
	public static readonly string TempPath = Path.GetTempPath() + "terramaster";
	public static readonly string StorePath = ApplicationData.Current.LocalFolder.Path;
	public static readonly string ConfigPath = Path.Combine(StorePath, "config.json");
	public static readonly string TerrServerUrl = "https://terramaster.flightgear.org/terrasync/";
	public static readonly string[] Ws2ServerUrls = ["https://terramaster.flightgear.org/terrasync/ws2/", "https://flightgear.sourceforge.net/scenery/", "https://de1mirror.flightgear.org/ws2/"];
	public static readonly string[] Ws3ServerUrls = ["https://terramaster.flightgear.org/terrasync/ws3/", "https://de1mirror.flightgear.org/ws3/"];
	public static readonly int OrthoRes = 4096;
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
	}

	public static (double lat, double lon) GetLatLon(int tileIndex)
	{
		// Extract x, y, baseY, baseX from the tile index
		int x = tileIndex & 0b111; // last 3 bits
		int y = (tileIndex >> 3) & 0b111111; // next 6 bits
		int baseY = ((tileIndex >> 6) & 0b1111111) - 90; // next 7 bits, then subtract 90
		int baseX = ((tileIndex >> 14) & 0b11111111) - 180; // next 8 bits, then subtract 180

		// You need to determine the tileWidth for this latitude band
		double lookup = Math.Abs(baseY);
		double tileWidth = 0;
		for (int i = 0; i < LatitudeIndex.GetLength(0); i++)
		{
			if (lookup >= LatitudeIndex[i, 0])
			{
				tileWidth = LatitudeIndex[i, 1];
				break;
			}
		}

		return (baseY + y / 8.0, baseX + x * tileWidth);
	}

	/// <summary>
	/// Gets the bounding box of a terrasync tile containing the given coordinates
	/// </summary>
	/// <param name="lat">The latitude of the point</param>
	/// <param name="lon">The longitude of the point</param>
	/// <returns>
	/// A 2x2 array, where the first element is the bottom left corner, and the second element is the top right corner.
	/// </returns>
	public static double[,] GetTileBounds(double lat, double lon)
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
		return bbox;
	}

	public static List<(double, double)> GetTilesWithinRadius(double lat, double lon, double radiusMiles)
	{
		List<(double, double)> result = [];

		double[,] tileBox = GetTileBounds(lat, lon);
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
				double[,] currentTileBox = GetTileBounds(i, j);
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

		double sdlat = Math.Sin((lat2 - lat1) / 2);
		double sdlon = Math.Sin((lon2 - lon1) / 2);
		double q = sdlat * sdlat + Math.Cos(lat1) * Math.Cos(lat2) * sdlon * sdlon;
		double d = 2 * r * Math.Asin(Math.Sqrt(q));
		return d;
	}

}