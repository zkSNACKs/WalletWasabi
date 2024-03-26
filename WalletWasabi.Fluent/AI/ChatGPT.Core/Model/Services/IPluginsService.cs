namespace ChatGPT.Model.Services;

public interface IPluginsService
{
    void DiscoverPlugins();
    void InitPlugins();
    void StartPlugins();
    void ShutdownPlugins();
}
