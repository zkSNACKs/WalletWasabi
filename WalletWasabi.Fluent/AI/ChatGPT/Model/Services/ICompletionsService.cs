using System.Threading;
using System.Threading.Tasks;
using AI.Model.Json.Completions;

namespace AI.Model.Services;

public interface ICompletionsService
{
    Task<CompletionsResponse?> GetResponseDataAsync(CompletionsServiceSettings settings, CancellationToken token);
}
