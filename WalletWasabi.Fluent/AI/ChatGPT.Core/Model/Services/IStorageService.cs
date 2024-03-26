using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace ChatGPT.Model.Services;

public interface IStorageService<T>
{
    Task SaveObjectAsync(T obj, string key, JsonTypeInfo<T> typeInfo);
    Task<T?> LoadObjectAsync(string key, JsonTypeInfo<T> typeInfo);
    void SaveObject(T obj, string key, JsonTypeInfo<T> typeInfo);
    T? LoadObject(string key, JsonTypeInfo<T> typeInfo);
}
