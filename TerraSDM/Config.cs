namespace TerraSDM;

public class Config
{
#pragma warning disable IDE0044
	private static string _savePath = "";
	private static int _queueSize = 20;
	private static int _orthoRes = 2048;
	private static string _cesiumToken = "";
	private static string[] _serverUrls = Util.Ws2ServerUrls;
	private static string _tileBorderColor = "";
#pragma warning restore IDE0044
	public static bool ReadConfig(string path)
	{
		if (!File.Exists(path) || new FileInfo(path).Length == 0 || path.EndsWith(".tsconfig"))
			return false;
		return true;
	}
}
