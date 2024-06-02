using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using ChatGPT.Model.Services;

namespace ChatGPT.Services;

public class ApplicationDataStorageService<T> : IStorageService<T>
{
    private const string FolderName = "ChatGPT";
    private const string FileExtension = ".json";

    public async Task SaveObjectAsync(T obj, string key, JsonTypeInfo<T> typeInfo)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appPath = Path.Combine(appDataPath, FolderName);
        if (!Directory.Exists(appPath))
        {
            Directory.CreateDirectory(appPath);
        }
        var appSettingPath = Path.Combine(appPath, key + FileExtension);
#if NETFRAMEWORK
        using var stream = File.Open(appSettingPath, FileMode.Create);
#else
        await using var stream = File.Open(appSettingPath, FileMode.Create);
#endif
        await JsonSerializer.SerializeAsync(stream, obj, typeInfo);
    }

    public async Task<T?> LoadObjectAsync(string key, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appSettingPath = Path.Combine(appDataPath, FolderName, key + FileExtension);
            if (File.Exists(appSettingPath))
            {
#if NETFRAMEWORK
                using var stream = File.OpenRead(appSettingPath);
#else
                await using var stream = File.OpenRead(appSettingPath);
#endif
                var storedObj = await JsonSerializer.DeserializeAsync(stream, typeInfo);
                return storedObj ?? default;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return default;
    }

    public void SaveObject(T obj, string key, JsonTypeInfo<T> typeInfo)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appPath = Path.Combine(appDataPath, FolderName);
        if (!Directory.Exists(appPath))
        {
            Directory.CreateDirectory(appPath);
        }
        var appSettingPath = Path.Combine(appPath, key + FileExtension);
        using var stream = File.Open(appSettingPath, FileMode.Create);
        JsonSerializer.Serialize(stream, obj, typeInfo);
    }

    public T? LoadObject(string key, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appSettingPath = Path.Combine(appDataPath, FolderName, key + FileExtension);
            if (File.Exists(appSettingPath))
            {
                using var stream = File.OpenRead(appSettingPath);
                var storedObj = JsonSerializer.Deserialize(stream, typeInfo);
                return storedObj ?? default;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return default;
    }
}
