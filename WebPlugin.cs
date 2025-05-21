using System.ComponentModel;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

public class WebPlugin
{
    private readonly KernelFactory kf;
    private readonly ILogger<WebPlugin> _logger;

    public WebPlugin(KernelFactory kf, ILogger<WebPlugin> logger)
    {
        this.kf = kf;
        _logger = logger;
    }

    [KernelFunction]
    [Description(
        "Search the internet using natural language. You'll receive a summary of what you search for."
    )]
    public async Task<string> Search(string search)
    {
        _logger.LogInformation($"Searching for: {search}");
        var kernel = kf.Create(LLMModel.Search);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddUserMessage(search);
        var result = await chatCompletionService.GetChatMessageContentAsync(
            history,
            kernel: kernel
        );
        _logger.LogInformation($"Search result: {result.Content}");
        return result.Content!;
    }

    // TODO: use playwright
    [KernelFunction]
    [Description(
        "Reads the content of a web page given its URL. Returns the raw text content of the page."
    )]
    public async Task<string> ReadPage(string url)
    {
        _logger.LogInformation($"Reading page: {url}");
        using var httpClient = new HttpClient();
        try
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Read {content.Length} characters from {url}");
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to read page: {url}");
            return $"Error reading page: {ex.Message}";
        }
    }
}
