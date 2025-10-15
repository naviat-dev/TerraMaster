namespace TerraMaster;

public class Config
{
	static string _savePath;
	static int _queueSize = 20;
	static int _orthoRes = 2048;
	static string _cesiumToken = "";
	static string[] _serverUrls = Util.Ws2ServerUrls;
}