using System.Threading;
using System.Threading.Tasks;
using ChatGPT;
using ChatGPT.ViewModels.Chat;

namespace WalletWasabi.Fluent.AI;

public static class Repl
{
	public static async Task RunAsync()
	{
		Defaults.ConfigureDefaultServices();

		using var cts = new CancellationTokenSource();

		var chat = new ChatViewModel(new ChatSettingsViewModel
		{
			Temperature = 0.7m,
			MaxTokens = 8000,
			Model = "",
			ApiUrl = "",
			// TODO: No api key for now
			ApiKey = null,
			Format = Defaults.TextMessageFormat,
		});

		// TODO: No api key for now
		chat.RequireApiKey = false;

		// TODO: System message hack
		chat.AddUserMessage("You are a Wasabi Wallet application helpdesk assistant.");
		chat.AddAssistantMessage("I'm Wasabi Wallet application helpdesk assistant, how can I help you.");

		while (true)
		{
			Console.Write("> ");

			var input = Console.ReadLine();
			if (string.IsNullOrWhiteSpace(input) || input == Environment.NewLine)
			{
				continue;
			}

			try
			{
				chat.AddUserMessage(input);
				var result = await chat.SendAsync(chat.CreateChatMessages(), cts.Token);
				chat.AddAssistantMessage(result?.Message);
				Console.WriteLine(result?.Message);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: " + ex.Message);
			}
		}
	}
}
