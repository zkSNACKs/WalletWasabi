using ChatGPT.Model.Services;

namespace ChatGPT.Services;

public class IsolatedStorageFactory : IStorageFactory
{
    public IStorageService<T> CreateStorageService<T>() => new IsolatedStorageService<T>();
}
