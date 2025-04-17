// See https://aka.ms/new-console-template for more information
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("Hello, World!");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (context, services) =>
        {
            services.AddSingleton<KernelFactory>();
            services.AddScoped<WebPlugin>();
services.AddScoped<DeveloperPlugin>();
services.AddScoped<AgentPlugin>();
        }
    )
    .Build();
var serviceProvider = host.Services;
var kf = serviceProvider.GetRequiredService<KernelFactory>();
var kernel = kf.Create(
    LLMModel.Large,
    typeof(WebPlugin), typeof(DeveloperPlugin), typeof(AgentPlugin)
);
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
Console.WriteLine(kernel.Plugins.Skip(1).First().GetType().Name);
var history = new ChatHistory();
history.AddSystemMessage(
    """
    You are an agent - please keep going until the user’s query is completely resolved, before ending your turn and yielding back to the user. Only terminate your turn when you are sure that the problem is solved.
    """
);
Console.WriteLine("You > ");
while (Console.ReadLine() is var input && !string.IsNullOrWhiteSpace(input))
{
    history.AddUserMessage(input);
    await foreach (
        var token in chatCompletionService.GetStreamingChatMessageContentsAsync(
            history,
            kernel: kernel,
            executionSettings: new() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(), }
        )
    )
    {
        Console.Write(token);
    }
    Console.WriteLine();
    Console.WriteLine($"[{history.Count}] You> ");
}
Console.WriteLine($"Done");







