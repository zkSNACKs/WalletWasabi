using System;
using System.Diagnostics;
using System.IO;
using System.IO.IsolatedStorage;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using ChatGPT.Model.Services;

namespace ChatGPT.Services;

public class IsolatedStorageService<T> : IStorageService<T>
{
    private static string Identifier { get; } = typeof(T).FullName?.Replace(".", string.Empty) ?? "default";

    public async Task SaveObjectAsync(T obj, string key, JsonTypeInfo<T> typeInfo)
    {
        var store = IsolatedStorageFile.GetStore(
            IsolatedStorageScope.User | IsolatedStorageScope.Domain | IsolatedStorageScope.Assembly, 
            null, null);
#if NETFRAMEWORK
        using var isoStream = new IsolatedStorageFileStream(Identifier + key, FileMode.Create, store);
#else
        await using var isoStream = new IsolatedStorageFileStream(Identifier + key, FileMode.Create, store);
#endif
        await JsonSerializer.SerializeAsync(isoStream, obj, typeInfo);
    }

    public async Task<T?> LoadObjectAsync(string key, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            var store = IsolatedStorageFile.GetStore(
                IsolatedStorageScope.User | IsolatedStorageScope.Domain | IsolatedStorageScope.Assembly, 
                null, null);
#if NETFRAMEWORK
            using var isoStream = new IsolatedStorageFileStream(Identifier + key, FileMode.Open, store);
#else
            await using var isoStream = new IsolatedStorageFileStream(Identifier + key, FileMode.Open, store);
#endif
            var storedObj = await JsonSerializer.DeserializeAsync(isoStream, typeInfo);
            return storedObj ?? default;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return default;
    }

    public void SaveObject(T obj, string key, JsonTypeInfo<T> typeInfo)
    {
        var store = IsolatedStorageFile.GetStore(
            IsolatedStorageScope.User | IsolatedStorageScope.Domain | IsolatedStorageScope.Assembly, 
            null, null);
        using var isoStream = new IsolatedStorageFileStream(Identifier + key, FileMode.Create, store);
        JsonSerializer.Serialize(isoStream, obj, typeInfo);
    }

    public T? LoadObject(string key, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            var store = IsolatedStorageFile.GetStore(
                IsolatedStorageScope.User | IsolatedStorageScope.Domain | IsolatedStorageScope.Assembly, 
                null, null); 
            using var isoStream = new IsolatedStorageFileStream(Identifier + key, FileMode.Open, store);
            var storedObj = JsonSerializer.Deserialize(isoStream, typeInfo);
            return storedObj ?? default;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return default;
    }
}
