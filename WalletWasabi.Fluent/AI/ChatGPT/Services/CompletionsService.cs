using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI.Model.Json.Completions;
using AI.Model.Services;

namespace AI.Services;

public class CompletionsService : ICompletionsService
{
    private static readonly HttpClient s_client;
    private static readonly string s_apiUrl = "https://api.openai.com/v1/completions";
    private readonly IChatSerializer _serializer;

    static CompletionsService()
    {
        s_client = new();
    }

    public CompletionsService(IChatSerializer serializer)
    {
        _serializer = serializer;
    }

    private string GetRequestBodyJson(CompletionsServiceSettings settings)
    {
        var model = settings.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = Environment.GetEnvironmentVariable(Constants.EnvironmentVariableApiModel);
        }

        // Set up the request body
        var requestBody = new CompletionsRequestBody
        {
            Model = model,
            Prompt = settings.Prompt,
            Suffix = null,
            MaxTokens = settings.MaxTokens,
            Temperature = settings.Temperature,
            TopP = settings.TopP,
            N = 1,
            Stream = false,
            Stop = settings.Stop,
            FrequencyPenalty = 0.0m,
            PresencePenalty = 0.0m,
            User = null
        };

        // Serialize the request body to JSON using the JsonSerializer.
        return _serializer.Serialize(requestBody);
    }

    private async Task<CompletionsResponse?> SendApiRequestAsync(string apiUrl, string apiKey, string requestBodyJson, CancellationToken token)
    {
        // Create a new HttpClient for making the API request

        // Set the API key in the request headers
        if (s_client.DefaultRequestHeaders.Contains("Authorization"))
        {
            s_client.DefaultRequestHeaders.Remove("Authorization");
        }
        s_client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        // Create a new StringContent object with the JSON payload and the correct content type
        var content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

        // Send the API request and get the response
        var response = await s_client.PostAsync(apiUrl, content, token);

        // Deserialize the response
#if NETFRAMEWORK
        var responseBody = await response.Content.ReadAsStringAsync();
#else
        var responseBody = await response.Content.ReadAsStringAsync(token);
#endif
        // Console.WriteLine($"Status code: {response.StatusCode}");
        // Console.WriteLine($"Response body:{Environment.NewLine}{responseBody}");
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
#if !NETFRAMEWORK
            case HttpStatusCode.TooManyRequests:
#endif
            case HttpStatusCode.NotFound:
            case HttpStatusCode.InternalServerError:
            case HttpStatusCode.BadRequest:
            {
                return _serializer.Deserialize<CompletionsResponseError>(responseBody);
            }
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        // Return the response data
        return _serializer.Deserialize<CompletionsResponseSuccess>(responseBody);
    }

    public async Task<CompletionsResponse?> GetResponseDataAsync(CompletionsServiceSettings settings, CancellationToken token)
    {
        // Set up the API URL and API key
        var apiKey = Environment.GetEnvironmentVariable(Constants.EnvironmentVariableApiKey);
        if (apiKey is null)
        {
            return null;
        }

        // Get the request body JSON
        var requestBodyJson = GetRequestBodyJson(settings);
  
        var apiUrl = s_apiUrl;
        var envApiUrl = Environment.GetEnvironmentVariable(Constants.EnvironmentVariableApiUrlCompletions);
        if (!string.IsNullOrWhiteSpace(envApiUrl))
        {
            apiUrl = envApiUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.Url))
        {
            apiUrl = settings.Url;
        }

        if (apiUrl is null)
        {
            return null;
        }

        // Send the API request and get the response data
        return await SendApiRequestAsync(apiUrl, apiKey, requestBodyJson, token);
    }
}
