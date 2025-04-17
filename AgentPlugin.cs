using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

public class AgentPlugin
{
    // DeveloperPlugin is referenced from DeveloperPlugin.cs

    private readonly KernelFactory kf;
    public AgentPlugin(KernelFactory kf) { this.kf = kf; }
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
        var kernel = kf.Create(LLMModel.Large, typeof(WebPlugin), typeof(DeveloperPlugin));
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
