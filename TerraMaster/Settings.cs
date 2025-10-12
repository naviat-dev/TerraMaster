using System.Text.Json;

public class Settings
{
    Dictionary<string, Dictionary<string, string>> settings;

    public Settings()
    {
        if(File.Exists(ApplicationData.Current.LocalFolder.Path + "/terramaster-settings.json"))
        {
            string json = File.ReadAllText(ApplicationData.Current.LocalFolder.Path + "/terramaster-settings.json");
            settings = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json) ?? [];
        }
        else
        {
            settings = [];
        }
    }

    public void SetSetting(string section, string key, string value)
    {
        if (!settings.ContainsKey(section))
        {
            settings[section] = [];
        }
        settings[section][key] = value;
    }

    public string GetSetting(string section, string key)
    {
        if (settings.ContainsKey(section) && settings[section].ContainsKey(key))
        {
            return settings[section][key];
        }
        return null;
    }
}