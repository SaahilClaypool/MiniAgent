using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

public class AgentPlugin
{
    // DeveloperPlugin is referenced from DeveloperPlugin.cs

    private readonly KernelFactory kf;
    private readonly ILogger<AgentPlugin> _logger;

    public AgentPlugin(KernelFactory kf, ILogger<AgentPlugin> logger)
    {
        this.kf = kf;
        _logger = logger;
    }

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
        _logger.LogInformation($"Starting subtask:\n{taskDefinition}");
        var kernel = kf.Create(LLMModel.Large, typeof(WebPlugin), typeof(DeveloperPlugin));
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(
            $"""
            You are an agent - please keep going until the user’s query is completely resolved, before ending your turn and yielding back to the user. Only terminate your turn when you are sure that the problem is solved.
            You MUST plan extensively before each function call, and reflect extensively on the outcomes of the previous function calls. DO NOT do this entire process by making function calls only, as this can impair your ability to solve the problem and think insightfully.

            Your final message should be a summary of the entire process, including the final result of the task.
            Finally, you MUST call the {nameof(StatePlugin)} {nameof(
                StatePlugin.Complete
            )} tool to indicate you have finished.
            Do NOT ask for user input - just finish when you have done the task to the best of your ability. If you have questions, return them in your final response.
            """
        );
        history.AddUserMessage(taskDefinition);
        var finished = false;
        var plugin = new StatePlugin(() =>
        {
            finished = true;
            return Task.CompletedTask;
        });
        kernel.Plugins.AddFromObject(plugin);
        var maxIterations = 10;
        while (
            !finished && history.Where(m => m.Role == AuthorRole.Assistant).Count() < maxIterations
        )
        {
            _logger.LogInformation("Working...");
            var result = await chatCompletionService.GetChatMessageContentAsync(
                history,
                kernel: kernel,
                executionSettings: new() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(), }
            );
            if (!finished) { }
            history.AddSystemMessage(
                $"call the {nameof(StatePlugin.Complete)} tool if you are finished. otherwise, keep going"
            );
            _logger.LogInformation($"----\n{result}\n-----");
        }
        return history.Where(m => m.Role == AuthorRole.Assistant).Last()?.Content ?? "No Content";
    }
}

public class StatePlugin(Func<Task> onComplete)
{
    [KernelFunction]
    [Description("Call this to mark the task as completed")]
    public async Task<string> Complete()
    {
        await onComplete();
        return "Completed";
    }
}
