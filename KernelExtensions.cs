using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

public static class KernelExtensions
{
    public static async Task<T> CompleteJson<T>(
        this IChatCompletionService svc,
        ChatHistory chat,
        Kernel? kernel = null
    )
    {
        OpenAIPromptExecutionSettings openAIPromptExecutionSettings =
            new()
            {
                FunctionChoiceBehavior =
                    kernel != null ? FunctionChoiceBehavior.Auto() : FunctionChoiceBehavior.None(),
                ResponseFormat = typeof(T),
            };
        var response = await svc.GetChatMessageContentAsync(
            chat,
            executionSettings: openAIPromptExecutionSettings,
            kernel: kernel
        );
        chat.Add(response);
        return (response.Content ?? "").FromJson<T>()!;
    }

    public static string ToJson(this object o) => System.Text.Json.JsonSerializer.Serialize(o);

    public static T? FromJson<T>(this string s) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(s);
}
