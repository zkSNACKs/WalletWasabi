namespace ChatGPT;

public static class Defaults
{
    public const string WelcomeMessage = "Hi! I'm Clippy, your Windows Assistant. Would you like to get some assistance?";

    public const decimal DefaultTemperature = 0.7m;

    public const decimal DefaultTopP = 1m;

    public const decimal DefaultPresencePenalty = 0m;

    public const decimal DefaultFrequencyPenalty = 0m;

    public const int DefaultMaxTokens = 2000;

    public const string DefaultModel = "gpt-3.5-turbo";
    
    public const string DefaultDirections = "You are a helpful assistant named Clippy. Write answers in Markdown blocks. For code blocks always define used language.";

    public const string DefaultMessageFormat = "Markdown";

    public const string TextMessageFormat = "Text";
    
    public const string MarkdownMessageFormat = "Markdown";
    
    public const string HtmlMessageTextFormat = "Html";
}
