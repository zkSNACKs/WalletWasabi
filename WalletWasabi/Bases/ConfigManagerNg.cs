using System.IO;
using System.Text;
using System.Text.Json;
using WalletWasabi.Interfaces;
using WalletWasabi.Logging;

namespace WalletWasabi.Bases;

public class ConfigManagerNg
{
	public static string ToFile<T>(string filePath, T obj, JsonSerializerOptions? options = null)
	{
		string jsonString = JsonSerializer.Serialize(obj, options);
		File.WriteAllText(filePath, jsonString, Encoding.UTF8);
		return jsonString;
	}

	/// <summary>
	/// Check if the config file differs from the config if the file path of the config file is set, otherwise throw exception.
	/// </summary>
	public static bool CheckFileChange<T>(string filePath, T current, JsonSerializerOptions? options = null)
		where T : IConfigNg, IDeepEqual<T>, new()
	{
		T diskVersion = LoadFile<T>(filePath, options);
		return !diskVersion.DeepEquals(current);
	}

	private static TResponse LoadFile<TResponse>(string filePath, JsonSerializerOptions? options = null)
	{
		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException($"File '{filePath}' does not exist.");
		}

		string jsonString = File.ReadAllText(filePath, Encoding.UTF8);
		TResponse? result = JsonSerializer.Deserialize<TResponse>(jsonString, options);

		return result is not null
			? result
			: throw new Newtonsoft.Json.JsonException("Unexpected null value.");
	}

	public static TResponse LoadFile<TResponse>(string filePath, bool createIfMissing = false, JsonSerializerOptions? options = null)
		where TResponse : IConfigNg, new()
	{
		TResponse result;

		if (!createIfMissing)
		{
			return LoadFile<TResponse>(filePath, options: options);
		}

		if (!File.Exists(filePath))
		{
			Logger.LogInfo($"File did not exist. Created at path: '{filePath}'.");
			result = new();
			ToFile(filePath, result, options);
		}
		else
		{
			try
			{
				return LoadFile<TResponse>(filePath, options: options);
			}
			catch (Exception ex)
			{
				result = new();
				ToFile(filePath, result, options);

				Logger.LogInfo($"File has been deleted because it was corrupted. Recreated default version at path: '{filePath}'.");
				Logger.LogWarning(ex);
			}
		}

		return result;
	}
}
