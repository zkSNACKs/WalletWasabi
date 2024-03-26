using System.Threading.Tasks;

namespace ChatGPT.Model.Plugins;

public interface IChatPlugin
{
    string Id { get; }
    string Name { get; }
    Task StartAsync();
    Task StopAsync();
    Task InitializeAsync(IPluginContext context);
    Task ShutdownAsync();
}
