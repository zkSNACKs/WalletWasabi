namespace ChatGPT.Model.Services;

public interface IStorageFactory
{
    IStorageService<T> CreateStorageService<T>();
}
