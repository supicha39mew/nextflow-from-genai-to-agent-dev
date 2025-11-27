using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.AI.Agents;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using Microsoft.Extensions.Configuration;
class Program
{
    static async Task Main(string[] args)
    {
        // Clear the console
        Console.Clear();

        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        string projectEndpoint = configuration["PROJECT_ENDPOINT"] ?? "";
        string modelDeployment = configuration["MODEL_DEPLOYMENT_NAME"] ?? "gpt-4o";

        // Display the data to be analyzed
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "data.txt");
        string data = File.ReadAllText(filePath) + "\n";
        Console.WriteLine(data);


        // Connect to the Agent client
        PersistentAgentsClient agentClient = new(projectEndpoint, new DefaultAzureCredential());


        // Upload the data file and create a CodeInterpreterTool
        PersistentAgentFileInfo uploadedFile = await agentClient.Files.UploadFileAsync(
            filePath: filePath,
            purpose: PersistentAgentFilePurpose.Agents
        );
        Console.WriteLine($"Uploaded {uploadedFile.Filename}");

        List<ToolDefinition> tools = [new CodeInterpreterToolDefinition()];


        // Define an agent that uses the CodeInterpreterTool
        var agent = await agentClient.Administration.CreateAgentAsync(
            modelDeployment,
            name: "data-agent",
            instructions: "You are an AI agent that analyzes the data in the file that has been uploaded. Use Python to calculate statistical metrics as necessary.",
            tools: tools,
            toolResources: new ToolResources
            {
                CodeInterpreter = new CodeInterpreterToolResource
                {
                    FileIds = { uploadedFile.Id }
                }
            }
        );
        Console.WriteLine($"Using agent: {agent.Value.Name}");


        // Create a thread for the conversation
        var thread = await agentClient.Threads.CreateThreadAsync();


        // Loop until the user types 'quit'
        while (true)
        {
            // Get input text
            Console.Write("Enter a prompt (or type 'quit' to exit): ");
            string? userPrompt = Console.ReadLine();

            if (string.IsNullOrEmpty(userPrompt))
            {
                Console.WriteLine("Please enter a prompt.");
                continue;
            }

            if (userPrompt.ToLower() == "quit")
            {
                break;
            }

            // Send a prompt to the agent
            await agentClient.Messages.CreateMessageAsync(
                threadId: thread.Value.Id,
                role: MessageRole.User,
                userPrompt
            );

            ThreadRun run = await agentClient.Runs.CreateRunAsync(
                thread.Value.Id,
                agent.Value.Id
            );

            // Wait for the run to complete
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                run = await agentClient.Runs.GetRunAsync(thread.Value.Id, run.Id);
            }
            while (run.Status == RunStatus.Queued
                || run.Status == RunStatus.InProgress);

            // Check the run status for failures
            if (run.Status == RunStatus.Failed)
            {
                Console.WriteLine($"Run failed: {run.LastError}");
            }


            // Show the latest response from the agent
            List<PersistentThreadMessage> messages = await agentClient.Messages.GetMessagesAsync(
                threadId: thread.Value.Id,
                order: ListSortOrder.Descending
            ).ToListAsync();

            var lastMessage = messages.FirstOrDefault(m => m.Role == MessageRole.Agent);

            if (lastMessage != null)
            {
                var content = lastMessage.ContentItems.OfType<MessageTextContent>().FirstOrDefault();
                if (content != null)
                {
                    Console.WriteLine($"Last Message: {content.Text}");
                }
            }


        }

        // Get the conversation history
        Console.WriteLine("\nConversation Log:\n");
        var allMessages = await agentClient.Messages.GetMessagesAsync(
                threadId: thread.Value.Id,
                order: ListSortOrder.Ascending
            ).ToListAsync();
    
        foreach (PersistentThreadMessage threadMessage in allMessages)
        {
            // Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");
            foreach (MessageContent contentItem in threadMessage.ContentItems)
            {
                if (contentItem is MessageTextContent textItem)
                {
                    Console.Write(textItem.Text);
                }
                Console.WriteLine();
            }
        }


        // Clean up
        // await agentClient.Threads.DeleteThreadAsync(thread.Value.Id);
        await agentClient.Administration.DeleteAgentAsync(agent.Value.Id);


    }
}
