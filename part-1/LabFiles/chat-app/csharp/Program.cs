// Add references
using System.Text;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using OpenAI.Chat;

const string EnvFileName = ".env";

Console.Clear();

try
{
    // Get configuration settings
    var settings = LoadSettings();

    var endpoint = ResolveSetting("PROJECT_ENDPOINT", settings);
    var modelDeployment = ResolveSetting("MODEL_DEPLOYMENT", settings);

    
    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new InvalidOperationException("PROJECT_ENDPOINT is not configured.");
    }
    
    if (string.IsNullOrWhiteSpace(modelDeployment))
    {
        throw new InvalidOperationException("MODEL_DEPLOYMENT is not configured.");
    }

    // Initialize the project client
    var projectClient = new AIProjectClient(
            new Uri(endpoint),
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = true,
                ExcludeManagedIdentityCredential = true
            }));

    // Get the Azure OpenAI client and chat client for the deployment
    var openAIClient = projectClient.GetOpenAIClient(apiVersion: "2024-10-21");
    var chatClient = openAIClient.GetChatClient(modelDeployment);
    

    // Initialize prompt with system message
    var conversation = new List<ChatMessage>
    {
        new SystemChatMessage("You are a helpful AI assistant that answers questions.")
    };

    // Loop until the user types 'quit'
    while (true)
    {
        Console.Write("Enter the prompt (or type 'quit' to exit): ");
        var inputText = Console.ReadLine();

        if (inputText is null)
        {
            continue;
        }

        if (string.Equals(inputText.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (string.IsNullOrWhiteSpace(inputText))
        {
            Console.WriteLine("Please enter a prompt.");
            continue;
        }

        // Get a chat completion
        conversation.Add(new UserChatMessage(inputText));

        var chatCompletion = await chatClient.CompleteChatAsync(
            conversation,
            new ChatCompletionOptions
            {
                Temperature = 0.8f
            });

        var completionText = ExtractContentText(chatCompletion);

        Console.WriteLine($"\nAssistant: {completionText}\n");

        conversation.Add(new AssistantChatMessage(completionText));

    }
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}

static string ExtractContentText(ChatCompletion completion)
{
    if (completion?.Content is null || completion.Content.Count == 0)
    {
        return string.Empty;
    }

    var builder = new StringBuilder();

    foreach (var part in completion.Content)
    {
        if (!string.IsNullOrEmpty(part.Text))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(part.Text);
        }
    }

    return builder.ToString();
}

static string? ResolveSetting(string key, IReadOnlyDictionary<string, string> settings)
{
    var envValue = Environment.GetEnvironmentVariable(key);
    if (!string.IsNullOrWhiteSpace(envValue))
    {
        return envValue;
    }

    if (settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
    {
        Environment.SetEnvironmentVariable(key, value);
        return value;
    }

    return null;
}

static Dictionary<string, string> LoadSettings()
{
    var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    string? envPath = null;

    try
    {
        envPath = ResolveEnvPath();
    }
    catch (FileNotFoundException)
    {
        return settings;
    }

    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
        {
            continue;
        }

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = trimmed[..separatorIndex].Trim();
        var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');
        settings[key] = value;
    }

    return settings;
}

static string ResolveEnvPath()
{
    var searchPaths = new[]
    {
        Path.Combine(Environment.CurrentDirectory, EnvFileName),
        Path.Combine(AppContext.BaseDirectory, EnvFileName),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", EnvFileName))
    };

    foreach (var path in searchPaths)
    {
        if (File.Exists(path))
        {
            return path;
        }
    }

    throw new FileNotFoundException($"Configuration file '{EnvFileName}' was not found in the application directory.");
}
