// See https://aka.ms/new-console-template for more information
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

Console.WriteLine("Hello, World!");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (context, services) =>
        {
            services.AddSingleton<KernelFactory>();
            services.AddScoped<WebPlugin>();
        }
    )
    .Build();
var serviceProvider = host.Services;
var kf = serviceProvider.GetRequiredService<KernelFactory>();
var kernel = kf.Create(
    LLMModel.Large,
    [typeof(WebPlugin), typeof(DeveloperPlugin), typeof(AgentPlugin)]
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
    var result = await chatCompletionService.GetChatMessageContentAsync(
        history,
        kernel: kernel,
        executionSettings: new() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(), }
    );
    Console.WriteLine(
        $"-----------------------------------------\nAssitant > {result.Content!}\n\n"
    );
    Console.WriteLine($"[{history.Count}] You> ");
}
Console.WriteLine($"Done");

public class KernelFactory(IConfiguration config, IServiceProvider services)
{
    static readonly ConcurrentDictionary<string, HttpClient> httpClients = new();

    public Kernel Create(LLMModel model, params IEnumerable<Type> plugins)
    {
        var (endpoint, apiKey, largeModel, smallModel, searchModel) = (
            config["Chat:Endpoint"] ?? throw new ArgumentException("Chat:Endpoint"),
            config["Chat:ApiKey"] ?? throw new ArgumentException("Chat:ApiKey"),
            config["Chat:LargeModel"] ?? throw new ArgumentException("Chat:LargeModel"),
            config["Chat:SmallModel"] ?? throw new ArgumentException("Chat:SmallModel"),
            config["Chat:SearchModel"] ?? throw new ArgumentException("Chat:SearchModel")
        );
        var builder = Kernel.CreateBuilder();
        var chatClient = httpClients.GetOrAdd(
            endpoint,
            (_) =>
            {
                var client = new HttpClient();
                client.BaseAddress = new Uri(endpoint);
                client.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost");
                client.DefaultRequestHeaders.Add("X-Title", "My Semantic Kernel App");
                return client;
            }
        );
        switch (model)
        {
            case LLMModel.Large:
                builder.AddOpenAIChatCompletion(largeModel, apiKey: apiKey, httpClient: chatClient);
                break;
            case LLMModel.Small:
                builder.AddOpenAIChatCompletion(smallModel, apiKey: apiKey, httpClient: chatClient);
                break;
            case LLMModel.Search:
                builder.AddOpenAIChatCompletion(
                    searchModel,
                    apiKey: apiKey,
                    httpClient: chatClient
                );
                break;
        }
        builder.Services.AddSingleton(services.GetRequiredService<ILoggerFactory>());

        builder.Services.AddScoped<KernelFactory>();
        foreach (var type in plugins)
        {
            var plugin =
                services.GetService(type)
                ?? ActivatorUtilities.CreateInstance(services, type)
                ?? throw new ArgumentException($"Failed to create service: {type.Name}");
            builder.Plugins.AddFromObject(plugin);
        }
        return builder.Build();
    }
}

public enum LLMModel
{
    Small,
    Large,
    Search,
    Thinking,
}

public class WebPlugin(KernelFactory kf)
{
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

public class DeveloperPlugin()
{
    [KernelFunction]
    [Description("Search local files using ripgrep")]
    public async Task<string> Rg(string search)
    {
        var rgPath = "rg";
        var arguments = $"\"{search}\" -C 2 --max-columns 200 ";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = rgPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Ripgrep failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    [KernelFunction]
    [Description("Read File")]
    public async Task<string> ReadFile(string path)
    {
        return File.ReadAllText(path);
    }

    [KernelFunction]
    [Description("list files")]
    public async Task<string> Ls(string path)
    {
        var files = string.Join(" ", Directory.GetFiles(path));
        var directories = string.Join(" ", Directory.GetDirectories(path));
        return $"Files: {files} Directories: {directories}";
    }

    [KernelFunction]
    [Description("Write File")]
    public async Task<string> WriteFile(string path, string content)
    {
        File.WriteAllText(path, content);
        return $"wrote content to {path}";
    }
}

public class AgentPlugin(KernelFactory kf)
{
    [KernelFunction]
    [Description(
        """
            Ask an agent to run a subtask for you. Give it a *detailed* and *specific* prompt of what you want it to do.
            Whenever you have a large problem, use this agent to do sub tasks so you can stay focused on the big picture.

            You should always make a plan for how thes subtasks will accomplish your main task before making them.
            """
    )]
    public async Task<string> StartSubtask(string taskDefinition)
    {
        var kernel = kf.Create(LLMModel.Large, [typeof(WebPlugin), typeof(DeveloperPlugin)]);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(
            """
            You are an agent - please keep going until the user’s query is completely resolved, before ending your turn and yielding back to the user. Only terminate your turn when you are sure that the problem is solved.
            You MUST plan extensively before each function call, and reflect extensively on the outcomes of the previous function calls. DO NOT do this entire process by making function calls only, as this can impair your ability to solve the problem and think insightfully.
            """
        );
        history.AddUserMessage(taskDefinition);
        var result = await chatCompletionService.GetChatMessageContentAsync(
            history,
            kernel: kernel,
            executionSettings: new() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(), }
        );
        return result.Content!;
    }
}
