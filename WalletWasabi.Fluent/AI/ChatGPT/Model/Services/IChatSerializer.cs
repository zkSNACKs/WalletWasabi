namespace AI.Model.Services;

public interface IChatSerializer
{
    string Serialize<T>(T value);
    T? Deserialize<T>(string json);
}
