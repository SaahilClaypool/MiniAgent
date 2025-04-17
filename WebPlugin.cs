using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

public class WebPlugin
{
    private readonly KernelFactory kf;
    public WebPlugin(KernelFactory kf) { this.kf = kf; }
    [KernelFunction]
    [Description(
        "Search the internet using natural language. You'll receive a summary of what you search for."
    )]
    public async Task<string> Search(string search)
    {
        var kernel = kf.Create(LLMModel.Search);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddUserMessage(search);
        var result = await chatCompletionService.GetChatMessageContentAsync(
            history,
            kernel: kernel
        );
        return result.Content!;
    }
}
