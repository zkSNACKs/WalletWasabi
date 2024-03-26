using System.Threading.Tasks;
using ChatGPT.Model.Plugins;

namespace ChatGPT.Plugins;

public class DummyChatPlugin : IChatPlugin
{
    public string Id => "Dummy";

    public string Name => "Dummy";

    public async Task StartAsync()
    {
        await Task.Yield();
    }

    public async Task StopAsync()
    {
        await Task.Yield();
    }

    public async Task InitializeAsync(IPluginContext context)
    {
        await Task.Yield();
    }

    public async Task ShutdownAsync()
    {
        await Task.Yield();
    }
}
