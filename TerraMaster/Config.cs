namespace TerraMaster;

public class Config
{
	private static readonly string _savePath;
	private static readonly int _queueSize = 20;
	private static readonly int _orthoRes = 2048;
	private static readonly string _cesiumToken = "";
	private static readonly string[] _serverUrls = Util.Ws2ServerUrls;
}
