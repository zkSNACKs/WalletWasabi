using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI.Model.Json.Chat;
using AI.Model.Services;

namespace AI.Services;

public class ChatService : IChatService
{
    private static readonly HttpClient s_client;
    private static readonly string s_apiUrl = "https://api.openai.com/v1/chat/completions";
    private readonly IChatSerializer _serializer;

    static ChatService()
    {
        s_client = new();
    }

    public ChatService(IChatSerializer serializer)
    {
        _serializer = serializer;
    }

    private string GetRequestBodyJson(ChatServiceSettings settings)
    {
        var model = settings.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = Environment.GetEnvironmentVariable(Constants.EnvironmentVariableApiModel);
        }

        // Set up the request body
        var requestBody = new ChatRequestBody
        {
            Model = model,
            Messages = settings.Messages,
            Functions = settings.Functions,
            FunctionCall = settings.FunctionCall,
            MaxTokens = settings.MaxTokens,
            Temperature = settings.Temperature,
            TopP = settings.TopP,
            N = 1,
            Stream = false,
            Stop = settings.Stop,
            FrequencyPenalty = settings.FrequencyPenalty,
            PresencePenalty = settings.PresencePenalty,
            User = null
        };

        // Serialize the request body to JSON using the JsonSerializer.
        return _serializer.Serialize(requestBody);
    }

    private async Task<ChatResponse?> SendApiRequestAsync(string apiUrl, string? apiKey, string requestBodyJson, bool debug, CancellationToken token)
    {
        // Create a new HttpClient for making the API request

        // Set the API key in the request headers
        if (s_client.DefaultRequestHeaders.Contains("Authorization"))
        {
            s_client.DefaultRequestHeaders.Remove("Authorization");
        }

        if (apiKey is not null)
        {
            s_client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        // Create a new StringContent object with the JSON payload and the correct content type
        var content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
        if (debug)
        {
            Console.WriteLine($"RequestBody:{Environment.NewLine}{requestBodyJson}");
        }

        // Send the API request and get the response
        var response = await s_client.PostAsync(apiUrl, content, token);

        // Deserialize the response
#if NETFRAMEWORK
        var responseBody = await response.Content.ReadAsStringAsync();
#else
        var responseBody = await response.Content.ReadAsStringAsync(token);
#endif
        if (debug)
        {
            Console.WriteLine($"Status code: {response.StatusCode}");
            Console.WriteLine($"Response body:{Environment.NewLine}{responseBody}");
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
#if !NETFRAMEWORK
            case HttpStatusCode.TooManyRequests:
#endif
            case HttpStatusCode.InternalServerError:
            case HttpStatusCode.NotFound:
            case HttpStatusCode.BadRequest:
            {
                return _serializer.Deserialize<ChatResponseError>(responseBody);
            }
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        // Return the response data
        return _serializer.Deserialize<ChatResponseSuccess>(responseBody);
    }

    public async Task<ChatResponse?> GetResponseDataAsync(ChatServiceSettings settings, CancellationToken token)
    {
        // Set up the API URL and API key
        var apiKey = Environment.GetEnvironmentVariable(Constants.EnvironmentVariableApiKey);
        if (apiKey is null && settings.RequireApiKey)
        {
            return null;
        }

        // Get the request body JSON
        var requestBodyJson = GetRequestBodyJson(settings);
  
        var apiUrl = s_apiUrl;
        var envApiUrl = Environment.GetEnvironmentVariable(Constants.EnvironmentVariableApiUrlChatCompletions);
        if (!string.IsNullOrWhiteSpace(envApiUrl))
        {
            apiUrl = envApiUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            apiUrl = settings.ApiUrl;
        }

        if (apiUrl is null)
        {
            return null;
        }

        var debug = settings.Debug;

        // Send the API request and get the response data
        return await SendApiRequestAsync(apiUrl, apiKey, requestBodyJson, debug, token);
    }
}
