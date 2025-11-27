using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;
using Microsoft.SemanticKernel.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Embeddings;

#region Index Schema

/// <summary>
/// Custom index schema. It may contain any fields that exist in search index.
/// </summary>
sealed class IndexSchema
{
    [JsonPropertyName("id")]
    public string ChunkId { get; set; }

    [JsonPropertyName("content")]
    public string Chunk { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("contentVector")]
    public ReadOnlyMemory<float> Vector { get; set; }
}

#endregion

#region Azure AI Search Service

/// <summary>
/// Abstraction for Azure AI Search service.
/// </summary>
interface IAzureAISearchService
{
    Task<string?> SearchAsync(
        string collectionName,
        ReadOnlyMemory<float> vector,
        List<string>? searchFields = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of Azure AI Search service.
/// </summary>
sealed class AzureAISearchService(SearchIndexClient indexClient) : IAzureAISearchService
{
    private readonly List<string> _defaultVectorFields = ["contentVector"];

    private readonly SearchIndexClient _indexClient = indexClient;

    public async Task<string?> SearchAsync(
        string collectionName,
        ReadOnlyMemory<float> vector,
        List<string>? searchFields = null,
        CancellationToken cancellationToken = default)
    {
        // Get client for search operations
        SearchClient searchClient = this._indexClient.GetSearchClient(collectionName);

        // Use search fields passed from Plugin or default fields configured in this class.
        List<string> fields = searchFields is { Count: > 0 } ? searchFields : this._defaultVectorFields;

        // Configure request parameters
        VectorizedQuery vectorQuery = new(vector);
        fields.ForEach(vectorQuery.Fields.Add);

        SearchOptions searchOptions = new() { VectorSearch = new() { Queries = { vectorQuery } } };

        // Perform search request
        Response<SearchResults<IndexSchema>> response = await searchClient.SearchAsync<IndexSchema>(searchOptions, cancellationToken);

        List<IndexSchema> results = [];

        // Collect search results
        await foreach (SearchResult<IndexSchema> result in response.Value.GetResultsAsync())
        {
            results.Add(result.Document);
        }

        // Return text from first result.
        // In real applications, the logic can check document score, sort and return top N results
        // or aggregate all results in one text.
        // The logic and decision which text data to return should be based on business scenario. 
        return results.FirstOrDefault()?.Chunk;
    }
}

#endregion

#region Azure AI Search SK Plugin

/// <summary>
/// Azure AI Search SK Plugin.
/// It uses <see cref="ITextEmbeddingGenerationService"/> to convert string query to vector.
/// It uses <see cref="IAzureAISearchService"/> to perform a request to Azure AI Search.
/// </summary>
sealed class MyAzureAISearchPlugin(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IAzureAISearchService searchService)
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = embeddingGenerator;
    private readonly IAzureAISearchService _searchService = searchService;

    [KernelFunction("Search")]
    public async Task<string> SearchAsync(
        string query,
        string collection,
        List<string>? searchFields = null,
        CancellationToken cancellationToken = default)
    {
        // Convert string query to vector
        ReadOnlyMemory<float> embedding = (await this._embeddingGenerator.GenerateAsync(query, cancellationToken: cancellationToken)).Vector;

        // Perform search
        return await this._searchService.SearchAsync(collection, embedding, searchFields, cancellationToken) ?? string.Empty;
    }
}

#endregion

class Program
{
    static async Task Main(string[] args)
    {
        #pragma warning disable SKEXP0010
        string filePath = Path.GetFullPath("appsettings.json");
        var config = new ConfigurationBuilder()
            .AddJsonFile(filePath)
            .Build();

        // 1. Create the Kernel Builder
        var builder = Kernel.CreateBuilder();

        // 2. Add Azure OpenAI Chat Completion
        builder.AddAzureOpenAIChatCompletion(
                deploymentName: config["DEPLOYMENT_NAME"]!,
                endpoint: config["AZURE_OPENAI_ENDPOINT"]!,
                apiKey: config["AZURE_OPENAI_KEY"]!
            );


        // 3. SearchIndexClient from Azure .NET SDK to perform search operations.
        builder.Services.AddSingleton<SearchIndexClient>((_) => new SearchIndexClient(
            new Uri(config["AZURE_SEARCH_ENDPOINT"]!), 
            new AzureKeyCredential(config["AZURE_SEARCH_KEY"]!)
        ));

        // 4. Custom AzureAISearchService to configure request parameters and make a request.
        builder.Services.AddSingleton<IAzureAISearchService, AzureAISearchService>();

        // 5. Embedding generation service to convert string query to vector
        builder.AddAzureOpenAIEmbeddingGenerator(
            deploymentName: config["EMBEDDING_DEPLOYMENT_NAME"]!, 
            endpoint: config["AZURE_OPENAI_ENDPOINT"]!, 
            apiKey: config["AZURE_OPENAI_KEY"]!
        );

        // 6. Register Azure AI Search Plugin
        builder.Plugins.AddFromType<MyAzureAISearchPlugin>();

        // 7. Create kernel
        var kernel = builder.Build();

        // 8. Invoke the prompt
        Console.Write("User: ");
        string query = Console.ReadLine()!;

        // Query with index name
        // The final prompt will look like this "Hotel... is ?".
        var result = await kernel.InvokePromptAsync(
        $"{{{{search '{query}' collection='{config["INDEX_NAME"]}'}}}} {query}");

        Console.WriteLine($"Assistant: {result}");
    }
}
