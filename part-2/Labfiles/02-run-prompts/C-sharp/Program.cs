using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

string filePath = Path.GetFullPath("appsettings.json");
var config = new ConfigurationBuilder()
    .AddJsonFile(filePath)
    .Build();

// Set your values in appsettings.json
string apiKey = config["AZURE_OPENAI_KEY"]!;
string endpoint = config["AZURE_OPENAI_ENDPOINT"]!;
string deploymentName = config["DEPLOYMENT_NAME"]!;

// Create a kernel with Azure OpenAI chat completion
var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
var kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Create the chat history
var chatHistory = new ChatHistory();


// Create a semantic kernel prompt template
var skTemplateFactory = new KernelPromptTemplateFactory();
var skPromptTemplate = skTemplateFactory.Create(new PromptTemplateConfig(
    """
    You are a helpful career advisor. Based on the users's skills and interest, suggest up to 5 suitable roles.
    Return the output as JSON in the following format:
    "Role Recommendations":
    {
    "recommendedRoles": [],
    "industries": [],
    "estimatedSalaryRange": ""
    }

    My skills are: {{$skills}}. My interests are: {{$interests}}. What are some roles that would be suitable for me?
    """
));

// Render the Semantic Kernel prompt with arguments
var skRenderedPrompt = await skPromptTemplate.RenderAsync(
    kernel,
    new KernelArguments
    {
        ["skills"] = "Software Engineering, C#, Python, Drawing, Guitar, Dance",
        ["interests"] = "Education, Psychology, Programming, Helping Others"
    }
);

// Add the Semantic Kernel prompt to the chat history and get the reply
chatHistory.AddUserMessage(skRenderedPrompt);
await GetReply();

// Create a handlebars template
var hbTemplateFactory = new HandlebarsPromptTemplateFactory();
var hbPromptTemplate = hbTemplateFactory.Create(new PromptTemplateConfig()
{
    TemplateFormat = "handlebars",
    Name = "MissingSkillsPrompt",
    Template = """
            <message role="system">
            Instructions: You are a career advisor. Analyze the skill gap between 
            the user's current skills and the requirements of the target role.
            </message>
            <message role="user">Target Role: {{targetRole}}</message>
            <message role="user">Current Skills: {{currentSkills}}</message>

            <message role="assistant">
            "Skill Gap Analysis":
            {
                "missingSkills": [],
                "coursesToTake": [],
                "certificationSuggestions": []
            }
            </message>
        """
    }
);

// Render the Handlebars prompt with arguments
var hbRenderedPrompt = await hbPromptTemplate.RenderAsync(
    kernel,
    new KernelArguments
    {
        ["targetRole"] = "Game Developer",
        ["currentSkills"] = "Software Engineering, C#, Python, Drawing, Guitar, Dance"
    }
);

// Add the Handlebars prompt to the chat history and get the reply
chatHistory.AddUserMessage(hbRenderedPrompt);
await GetReply();


// Get a follow-up prompt from the user
Console.WriteLine("Assistant: How can I help you?");
Console.Write("User: ");
string input = Console.ReadLine()!;

// Add the user input to the chat history and get the reply
chatHistory.AddUserMessage(input);
await GetReply();

async Task GetReply() {
    // Get the reply from the chat completion service
    ChatMessageContent reply = await chatCompletionService.GetChatMessageContentAsync(
        chatHistory,
        kernel: kernel
    );
    Console.WriteLine("Assistant: " + reply.ToString());
    chatHistory.AddAssistantMessage(reply.ToString());
}