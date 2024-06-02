using ChatGPT.Model.Services;

namespace ChatGPT.Services;

public class ApplicationDataStorageFactory : IStorageFactory
{
    public IStorageService<T> CreateStorageService<T>() => new ApplicationDataStorageService<T>();
}
