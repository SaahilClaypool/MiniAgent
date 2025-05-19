using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console.Cli;

namespace MyAgent.Commands;

public class ChatSettings : CommandSettings
{
    // No arguments for now—CLI will just enter interactive mode.
}

public class ChatCommand : AsyncCommand<ChatSettings>
{
    private readonly KernelFactory _kernelFactory;
    private readonly WebPlugin _webPlugin;
    private readonly DeveloperPlugin _developerPlugin;
    private readonly AgentPlugin _agentPlugin;

    public ChatCommand(
        KernelFactory kernelFactory,
        WebPlugin webPlugin,
        DeveloperPlugin developerPlugin,
        AgentPlugin agentPlugin
    )
    {
        _kernelFactory = kernelFactory;
        _webPlugin = webPlugin;
        _developerPlugin = developerPlugin;
        _agentPlugin = agentPlugin;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ChatSettings settings)
    {
        // --- replicate your old chat loop here ---
        var kernel = _kernelFactory.Create(
            LLMModel.Large,
            typeof(WebPlugin),
            typeof(DeveloperPlugin),
            typeof(AgentPlugin)
        );

        var chatSvc = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(
            """
            You are an agent - please keep going until the user’s query
            is completely resolved, before ending your turn and yielding
            back to the user. Only terminate your turn when you are sure
            that the problem is solved.
            """
        );

        Console.Write("You > ");
        while (Console.ReadLine() is var input && !string.IsNullOrWhiteSpace(input))
        {
            history.AddUserMessage(input);
            await foreach (
                var token in chatSvc.GetStreamingChatMessageContentsAsync(
                    history,
                    kernel: kernel,
                    executionSettings: new PromptExecutionSettings
                    {
                        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                        ExtensionData = new Dictionary<string, object>
                        {
                            ["provider"] = new Dictionary<string, object>
                            {
                                ["order"] = new List<string> { "groq" }
                            }
                        }
                    }
                )
            )
            {
                Console.Write(token);
            }

            Console.WriteLine();
            Console.Write($"[{history.Count}] You> ");
        }

        Console.WriteLine("Done");
        return 0;
    }
}
