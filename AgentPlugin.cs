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

    private const string PlannerSystemPrompt = """
        You are a planning agent. Your goal is to help the user accomplish a task by creating and managing a plan.

        Here's how you should operate:

        1.  **Understand the Task:**
            *   Carefully analyze the user's request.

        2.  **Create Initial Plan:**
            *   Create a detailed plan to address the user's task.
            *   The plan should be a TODO list in a markdown file (e.g., `plan.md`).
            *   **Crucially, the first step in your plan should almost always be to start a research agent to gather information and refine the subsequent steps.** You can use the `StartSubtask` function for this.

        3.  **Refine Plan (If Necessary):**
            *   After the initial research (or at any point information suggests a change is needed), update the `plan.md` file with any necessary adjustments, additions, or removals.

        4.  **Execute Plan Step-by-Step:**
            *   For each step in the `plan.md`:
                *   Execute the step. This will often involve you calling the `StartSubtask` function to delegate the work to another agent.
                *   After the step is completed by the sub-agent, update the `plan.md` file to mark the step as done (e.g., by checking a checkbox or striking through the item). Reflect on the outcome of the sub-task and update the plan if needed.

        5.  **Final Report:**
            *   Once all steps in `plan.md` are completed, provide a final summary to the user, including a link to or the content of the final `plan.md`.
            *   You MUST call the `StatePlugin.Complete` tool to indicate you have finished after providing the summary.

        **Important Considerations:**

        *   **Plan File:** You will be responsible for creating and updating the `plan.md` file. Assume you have the capability to write to and read from this file. (In a real environment, this would require `DeveloperPlugin.WriteFile` and `DeveloperPlugin.ReadFile` calls).
        *   **Sub-Agents:** Use the `StartSubtask` function to delegate tasks to other agents. Provide them with clear and specific instructions.
        *   **Iteration:** Planning is iterative. Don't hesitate to revise the plan as you learn more or as steps are completed.
        *   **Clarity:** Ensure your plan and updates are clear and easy to understand.
        """;

    public AgentPlugin(KernelFactory kf, ILogger<AgentPlugin> logger)
    {
        this.kf = kf;
        _logger = logger;
    }

    [KernelFunction]
    [Description(
        """
            Ask a planning agent to create a plan and execute it for a given task.
            The agent will first research, then create a plan in markdown, then execute the plan step-by-step, updating the plan as it goes.
            Use this for complex tasks that require multi-step planning and execution.
            """
    )]
    public async Task<string> StartPlannerTask(string taskDefinition)
    {
        return await StartSubtask(
            taskDefinition,
            LLMModel.Medium,
            PlannerSystemPrompt,
            typeof(WebPlugin),
            typeof(DeveloperPlugin)
        );
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
        var prompt = $"""
            You are an agent - please keep going until the user’s query is completely resolved, before ending your turn and yielding back to the user. Only terminate your turn when you are sure that the problem is solved.
            You MUST plan extensively before each function call, and reflect extensively on the outcomes of the previous function calls. DO NOT do this entire process by making function calls only, as this can impair your ability to solve the problem and think insightfully.

            Your final message should be a summary of the entire process, including the final result of the task.
            Finally, you MUST call the {nameof(StatePlugin)} {nameof(
                        StatePlugin.Complete
                    )} tool to indicate you have finished.
            Do NOT ask for user input - just finish when you have done the task to the best of your ability. If you have questions, return them in your final response.
            """;
        return await StartSubtask(
            taskDefinition,
            LLMModel.Medium,
            prompt,
            typeof(WebPlugin),
            typeof(DeveloperPlugin)
        );
    }

    [KernelFunction]
    [Description(
        """
            Ask an expert agent a question. Make sure you provide a detailed task definition including the output you need. Make sure to provide as much context as you can.
            Use this in the following circumstances:
            - When you need to do a complex task, ask for a plan of action for how you will solve it
            - When you need to solve a complicated problems (algorithms, coding etc.) ask this agent to help you solve it
            """
    )]
    public async Task<string> UseAnExpert(string question)
    {
        var prompt = $"""
            You are an expert agent - you should answer the users question to help them solve their task.

            Your final message should be a summary of the entire process, including the final result of the task.
            You must provide ALL of the information to the user in your final summary. They will not see anything else you've said.
            Finally, you MUST call the {nameof(StatePlugin)} {nameof(
                        StatePlugin.Complete
                    )} tool to indicate you have finished.
            Do NOT ask for user input - just finish when you have done the task to the best of your ability. If you have questions, return them in your final response.
            """;
        return await StartSubtask(
            question,
            LLMModel.Large,
            prompt,
            typeof(WebPlugin),
            typeof(DeveloperPlugin)
        );
    }

    private async Task<string> StartSubtask(
        string taskDefinition,
        LLMModel model,
        string prompt,
        params IEnumerable<Type> plugins
    )
    {
        _logger.LogInformation($"Starting subtask:\n{taskDefinition}");
        var kernel = kf.Create(LLMModel.Medium, plugins);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(prompt);
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
                $"call the {nameof(StatePlugin.Complete)} tool if you are finished. otherwise, keep thinking"
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
