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
    [Description("Get repository symbol overview")]
    public async Task<string> RepositoryOverview()
    {
        var psi = new ProcessStartInfo("aider", "--show-repo-map")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        var proc = Process.Start(psi);
        var repoMap = await proc!.StandardOutput.ReadToEndAsync();
        return repoMap;
    }

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
    [Description(
        """
            Use the tool to think about something. It will not obtain new information or make any changes to the repository, but just log the thought. Use it when complex reasoning or brainstorming is needed. 

            Common use cases:
            1. When exploring a repository and discovering the source of a bug, call this tool to brainstorm several unique ways of fixing the bug, and assess which change(s) are likely to be simplest and most effective
            2. After receiving test results, use this tool to brainstorm ways to fix failing tests
            3. When planning a complex refactoring, use this tool to outline different approaches and their tradeoffs
            4. When designing a new feature, use this tool to think through architecture decisions and implementation details
            5. When debugging a complex issue, use this tool to organize your thoughts and hypotheses

            The tool simply logs your thought process for better transparency and does not execute any code or make changes.
            """
    )]
    public async Task<string> Think(string thought)
    {
        return $"Your thought has been logged";
    }

    [KernelFunction]
    [Description("Read File")]
    public async Task<string> ReadFile(string path)
    {
        return File.ReadAllText(path);
    }

    [KernelFunction]
    [Description("list files")]
    public async Task<string> ListFiles(string path)
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

    [KernelFunction]
    [Description(
        """
            Edit a file by providing the path, the text to replace, and the replacement text.
            You should *almost always* use this over `WriteFile` to avoid overwriting the entire file.
            """
    )]
    public async Task<string> EditFile(string path, string replace, string with)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
        var content = File.ReadAllText(path);
        if (!content.Contains(replace))
        {
            // send replace to the closest span by finding the chunk of text with the closest edit distance.
            // if the replacement text is multiple lines, then just find the line with the lowest edit distance to the first line
            var lines = content.Split('\n');
            var replacementLines = replace.Split('\n');
            var firstLine = replacementLines[0];
            var closestLine = lines
                .Select(
                    (line, idx) =>
                        (line, distance: LevenshteinDistance(line.Trim(), firstLine.Trim()), idx)
                )
                .OrderBy(x => x.distance)
                .FirstOrDefault();
            if (closestLine.distance > (replace.Length / 2))
            {
                // if the closest line is more than half the length of the replacement text, then we should not replace it
                // this is a heuristic to avoid replacing text that is not similar enough
                {
                    throw new ArgumentException(
                        $"The text to replace was not found in the file. The closest match was: {closestLine.line}"
                    );
                }
            }
            // if the closest line is less than half the length of the replacement text, then we should replace it
            replace = string.Join(
                "\n",
                lines[
                    closestLine.idx..new[]
                    {
                        lines.Length,
                        closestLine.idx + 1,
                        replacementLines.Length + closestLine.idx
                    }.Min()
                ]
            );
        }
        content = content.Replace(replace, with);
        File.WriteAllText(path, content);
        return $"wrote content to {path}";

        int LevenshteinDistance(string s, string t)
        {
            var d = new int[s.Length + 1, t.Length + 1];
            for (var i = 0; i <= s.Length; i++)
            {
                d[i, 0] = i;
            }
            for (var j = 0; j <= t.Length; j++)
            {
                d[0, j] = j;
            }
            for (var i = 1; i <= s.Length; i++)
            {
                for (var j = 1; j <= t.Length; j++)
                {
                    var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }
            return d[s.Length, t.Length];
        }
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

            Your final message should be a summary of the entire process, including the final result of the task.
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
