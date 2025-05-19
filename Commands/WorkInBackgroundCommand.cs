using System;
using System.ComponentModel; // for DescriptionAttribute
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console.Cli;

namespace MyAgent.Commands
{
    // ─────────────────────────────────────────────────────────────────────────────
    // 1) Native plugins (strongly-typed methods)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Generates a slug-safe Git branch name from a task description.</summary>
    public sealed class BranchPlugin
    {
        [KernelFunction, Description("Return a short, slug-safe branch name")]
        public string GenerateBranchSlug([Description("Task description")] string task)
        {
            // Simple local slug-generation; fallback to GUID if empty
            var slug = Regex.Replace(task.ToLowerInvariant(), @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"\s+", " ").Trim();
            slug = string.Join('-', slug.Split(' ').Take(6));
            return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N") : slug;
        }
    }

    /// <summary>Evaluates whether the conversation indicates task completion.</summary>
    public sealed class EvalPlugin
    {
        [KernelFunction, Description("Return true if the user’s task is done, else false")]
        public bool IsTaskDone([Description("Full conversation so far")] string conversation)
        {
            // Simple heuristic; replace with richer logic or an LLM semantic function if desired
            return conversation.Contains("[DONE]", StringComparison.OrdinalIgnoreCase);
        }

        [KernelFunction, Description("Summarise completed work for the user")]
        public string Complete([Description("Original task")] string task) =>
            $"✅ Task “{task}” finished and committed. All set!";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2) CLI command
    // ─────────────────────────────────────────────────────────────────────────────

    public class WorkInBackgroundSettings : CommandSettings
    {
        [CommandArgument(0, "<task>")]
        public string Task { get; set; } = string.Empty;
    }

    public sealed class WorkInBackgroundCommand : AsyncCommand<WorkInBackgroundSettings>
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
                // a) Build kernel and register plugins
                var kernel = _kernelFactory.Create(
                    LLMModel.Large,
                    typeof(WebPlugin),
                    typeof(DeveloperPlugin),
                    typeof(AgentPlugin)
                );

                kernel.Plugins.AddFromType<BranchPlugin>("Branch");
                kernel.Plugins.AddFromType<EvalPlugin>("Eval");

                // b) Generate branch name natively
                var slug = await kernel.InvokeAsync<string>(
                    "Branch",
                    "GenerateBranchSlug",
                    new KernelArguments { ["task"] = settings.Task }
                );

                var branchName = $"bg-{slug}";
                var worktreePath = GitHelper.CreateWorktree(branchName);
                Console.WriteLine($"Created worktree at: {worktreePath}");
                Directory.SetCurrentDirectory(worktreePath);

                // c) Prepare chat-completion service
                var chatSvc = kernel.GetRequiredService<IChatCompletionService>();

                var history = new ChatHistory();
                history.AddSystemMessage(
                    "You are a background agent. Continue working until the task is solved. "
                        + "Mark your final answer with [DONE]."
                );
                history.AddUserMessage(settings.Task);

                var buffer = new StringBuilder();

                while (true)
                {
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
                        buffer.Append(chunk);
                    }
                    Console.WriteLine();

                    history.AddAssistantMessage(buffer.ToString());
                    buffer.Clear();

                    // d) Ask EvalPlugin if we’re done
                    var isDone = await kernel.InvokeAsync<bool>(
                        "Eval",
                        "IsTaskDone",
                        new KernelArguments { ["conversation"] = history.ToString() }
                    );

                    if (isDone)
                    {
                        var summary = await kernel.InvokeAsync<string>(
                            "Eval",
                            "Complete",
                            new KernelArguments { ["task"] = settings.Task }
                        );
                        Console.WriteLine(summary);
                        break;
                    }
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
