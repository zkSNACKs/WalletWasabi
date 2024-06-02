namespace AI.Model.Services;

public interface ICompletionsSerializer
{
    string Serialize<T>(T value);
    T? Deserialize<T>(string json);
}
