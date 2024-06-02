using System.Threading;
using System.Threading.Tasks;
using AI.Model.Json.Chat;

namespace AI.Model.Services;

public interface IChatService
{
    Task<ChatResponse?> GetResponseDataAsync(ChatServiceSettings settings, CancellationToken token);
}
