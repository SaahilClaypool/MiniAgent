using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

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
}
