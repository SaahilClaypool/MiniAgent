using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console.Cli;

namespace MyAgent.Commands
{
    public class WorkInBackgroundSettings : CommandSettings
    {
        [CommandArgument(0, "<task>")]
        public string Task { get; set; } = string.Empty;
    }

    public class WorkInBackgroundCommand : AsyncCommand<WorkInBackgroundSettings>
    {
        private readonly KernelFactory _kernelFactory;
        private readonly WebPlugin _web;
        private readonly DeveloperPlugin _dev;
        private readonly AgentPlugin _agent;

        public WorkInBackgroundCommand(
            KernelFactory kernelFactory,
            WebPlugin web,
            DeveloperPlugin dev,
            AgentPlugin agent
        )
        {
            _kernelFactory = kernelFactory;
            _web = web;
            _dev = dev;
            _agent = agent;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            WorkInBackgroundSettings settings
        )
        {
            try
            {
                // 1) Create kernel (we’ll also use it below)
                var kernel = _kernelFactory.Create(
                    LLMModel.Large,
                    typeof(WebPlugin),
                    typeof(DeveloperPlugin),
                    typeof(AgentPlugin)
                );

                // 2) Define a prompt function to generate a concise branch slug
                var generateBranchName = kernel.CreateFunctionFromPrompt(
                    @"You are a Git branch name generator. Given the task description, return a short branch name containing only lowercase letters, numbers and hyphens (no spaces, no prefix).",
                    new PromptExecutionSettings(),
                    functionName: "GenerateBranchName",
                    description: "Generate a concise branch slug from a task"
                );

                // 3) Invoke it
                var branchResult = await kernel.InvokeAsync(
                    generateBranchName,
                    new KernelArguments { ["task"] = settings.Task }
                );
                // 4) Fallback to GUID if LLM fails, then prefix with "bg-"
                var slug = branchResult.GetValue<string>()
                                 ?.Trim().ToLower().Replace(" ", "-")
                                 ?? Guid.NewGuid().ToString("N");
                var branchName = $"bg-{slug}";

                // 5) Now create the worktree
                string worktreePath = GitHelper.CreateWorktree(branchName);

                Console.WriteLine($"Created worktree at: {worktreePath}");

                // Change directory to the worktree
                Directory.SetCurrentDirectory(worktreePath);
                Console.WriteLine($"Changed directory to worktree: {worktreePath}");

                // 2) Prepare chat completion service
                var chatSvc = kernel.GetRequiredService<IChatCompletionService>();

                // 3) Define two functions:
                //    a) isDoneEvaluator: examines the chat history and returns "true" or "false"
                var isDoneEvaluator = kernel.CreateFunctionFromPrompt(
                    @"You are an evaluator. Given the conversation so far, return exactly 'true' if the user's task is complete, otherwise 'false'.",
                    new PromptExecutionSettings { },
                    functionName: "IsDoneEvaluator",
                    description: "Checks if the background task is complete"
                );

                // 4) Initialize chat history
                var history = new ChatHistory();
                history.AddSystemMessage(
                    "You are a background agent. Continue working on the user's request until it is solved."
                );
                history.AddUserMessage(settings.Task);

                // 5) Loop: stream assistant responses, then ask evaluator if done
                while (true)
                {
                    // stream the chat response
                    await foreach (
                        var chunk in chatSvc.GetStreamingChatMessageContentsAsync(
                            history,
                            kernel: kernel,
                            executionSettings: new PromptExecutionSettings
                            {
                                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                            }
                        )
                    )
                    {
                        Console.Write(chunk);
                    }
                    Console.WriteLine();

                    // add the assistant's last response into history
                    history.AddAssistantMessage(string.Empty); // spectre workaround: real implementation may capture from buffer

                    // 6) Ask the evaluator if we're done
                    var evalResult = await kernel.InvokeAsync(
                        isDoneEvaluator,
                        new KernelArguments { ["input"] = history.ToString() }
                    );
                    bool isDone = bool.TryParse(evalResult.GetValue<string>(), out var b) && b;
                    if (isDone)
                    {
                        // 7) Invoke Complete() to commit and summarize
                        var completeResult = await kernel.InvokeAsync(
                            isDoneEvaluator,
                            new KernelArguments { ["input"] = settings.Task }
                        );
                        Console.WriteLine(completeResult.GetValue<string>());
                        break;
                    }
                    // else, continue looping – evaluator wants more work
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }
}
