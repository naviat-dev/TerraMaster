using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerraSDM;

public class ConfigData
{
	[JsonPropertyName("savePath")]
	public string SavePath { get; set; } = "";

	[JsonPropertyName("queueSize")]
	public int QueueSize { get; set; } = 20;

	[JsonPropertyName("orthoRes")]
	public int OrthoRes { get; set; } = 2048;

	[JsonPropertyName("cesiumToken")]
	public string CesiumToken { get; set; } = "";

	[JsonPropertyName("serverUrls")]
	public string[] ServerUrls { get; set; } = Util.Ws2ServerUrls;

	[JsonPropertyName("tileBorderColor")]
	public string TileBorderColor { get; set; } = "";
}

public static class Config
{
	private static ConfigData _data = new();
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	// Properties for backward compatibility and easy access
	public static string SavePath
	{
		get => _data.SavePath;
		set => _data.SavePath = value;
	}

	public static int QueueSize
	{
		get => _data.QueueSize;
		set => _data.QueueSize = value;
	}

	public static int OrthoRes
	{
		get => _data.OrthoRes;
		set => _data.OrthoRes = value;
	}

	public static string CesiumToken
	{
		get => _data.CesiumToken;
		set => _data.CesiumToken = value;
	}

	public static string[] ServerUrls
	{
		get => _data.ServerUrls;
		set => _data.ServerUrls = value;
	}

	public static string TileBorderColor
	{
		get => _data.TileBorderColor;
		set => _data.TileBorderColor = value;
	}

	/// <summary>
	/// Reads configuration from the specified JSON file
	/// </summary>
	/// <param name="path">Path to the config file</param>
	/// <returns>True if config was successfully loaded, false otherwise</returns>
	public static bool ReadConfig(string path)
	{
		try
		{
			if (!File.Exists(path) || new FileInfo(path).Length == 0)
				return false;

			string jsonString = File.ReadAllText(path);
			ConfigData? loadedData = JsonSerializer.Deserialize<ConfigData>(jsonString, _jsonOptions);

			if (loadedData != null)
			{
				_data = loadedData;
				return true;
			}
		}
		catch (Exception ex)
		{
			Logger.Error("Config", "Error reading config", ex);
		}
		return false;
	}

	/// <summary>
	/// Saves the current configuration to the specified JSON file
	/// </summary>
	/// <param name="path">Path to save the config file</param>
	/// <returns>True if config was successfully saved, false otherwise</returns>
	public static bool SaveConfig(string path)
	{
		try
		{
			// Ensure directory exists
			string? directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			string jsonString = JsonSerializer.Serialize(_data, _jsonOptions);
			File.WriteAllText(path, jsonString);
			Logger.Info("Config", $"Configuration saved to {path}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error("Config", "Error saving config", ex);
			return false;
		}
	}

	/// <summary>
	/// Loads config from the default location (Util.ConfigPath)
	/// </summary>
	/// <returns>True if config was successfully loaded</returns>
	public static bool Load()
	{
		return ReadConfig(Util.ConfigPath);
	}

	/// <summary>
	/// Saves config to the default location (Util.ConfigPath)
	/// </summary>
	/// <returns>True if config was successfully saved</returns>
	public static bool Save()
	{
		return SaveConfig(Util.ConfigPath);
	}
}
