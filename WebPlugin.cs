using System.ComponentModel;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
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

    [KernelFunction]
    [Description(
        "Reads the content of a web page given its URL. Set useBrowser to true to use a browser (for dynamic or JavaScript-heavy pages), or false to use a simple HTTP GET request (for static pages). Returns the raw text content of the page."
    )]
    public async Task<string> ReadPage(string url, bool useBrowser = true)
    {
        _logger.LogInformation($"Reading page: {url} (useBrowser={useBrowser})");
        if (!useBrowser)
        {
            try
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation(
                    $"Read {content.Length} characters from {url} using HTTP GET"
                );
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to read page with HTTP GET: {url}");
                return $"Error reading page with HTTP GET: {ex.Message}";
            }
        }
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions { Headless = true }
            );
            var page = await browser.NewPageAsync();

            await page.GotoAsync(url);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded); // Using DOMContentLoaded for potentially faster load, adjust if needed

            var content = await page.ContentAsync();
            _logger.LogInformation($"Read {content.Length} characters from {url} using browser");
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to read page: {url}");
            return $"Error reading page: {ex.Message}";
        }
    }
}
