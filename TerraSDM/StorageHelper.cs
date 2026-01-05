namespace TerraSDM;

/// <summary>
/// Helper class for file and folder operations in the scenery directory
/// </summary>
public static class StorageHelper
{
	/// <summary>
	/// Creates a folder in the scenery directory
	/// </summary>
	public static bool CreateSceneryFolder(string relativePath)
	{
		try
		{
			string fullPath = Path.Combine(Util.SavePath, relativePath);
			if (!Directory.Exists(fullPath))
			{
				Directory.CreateDirectory(fullPath);
			}
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error("StorageHelper", $"Error creating scenery folder '{relativePath}'", ex);
			return false;
		}
	}

	/// <summary>
	/// Writes bytes to a file in the scenery directory
	/// </summary>
	public static async Task<bool> WriteSceneryBytesAsync(string relativePath, byte[] buffer)
	{
		try
		{
			string fullPath = Path.Combine(Util.SavePath, relativePath);
			string? directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			await File.WriteAllBytesAsync(fullPath, buffer);
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error("StorageHelper", $"Error writing scenery bytes file '{relativePath}'", ex);
			return false;
		}
	}

	/// <summary>
	/// Writes text to a file in the scenery directory
	/// </summary>
	public static async Task<bool> WriteSceneryTextAsync(string relativePath, string content)
	{
		try
		{
			string fullPath = Path.Combine(Util.SavePath, relativePath);
			string? directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			await File.WriteAllTextAsync(fullPath, content);
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error("StorageHelper", $"Error writing scenery text file '{relativePath}'", ex);
			return false;
		}
	}

	/// <summary>
	/// Gets the base path for scenery storage
	/// </summary>
	public static string GetSceneryBasePath()
	{
		return Util.SavePath;
	}

	/// <summary>
	/// Checks if scenery folder is accessible
	/// </summary>
	public static bool IsSceneryFolderAccessible()
	{
		try
		{
			// Check if Config.SavePath is accessible
			if (!string.IsNullOrEmpty(Config.SavePath))
			{
				return Directory.Exists(Config.SavePath) || Directory.GetParent(Config.SavePath)?.Exists == true;
			}

			return false;
		}
		catch
		{
			return false;
		}
	}
}
