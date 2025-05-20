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
    public class WorkInBackgroundSettings : CommandSettings
    {
        [CommandArgument(0, "<task>")]
        public string Task { get; set; } = string.Empty;
    }

    public sealed class WorkInBackgroundCommand(AgentPlugin agentPlugin, KernelFactory kf)
        : AsyncCommand<WorkInBackgroundSettings>
    {
        public override async Task<int> ExecuteAsync(
            CommandContext context,
            WorkInBackgroundSettings settings
        )
        {
            try
            {
                var slug = await CreateBranchName(settings.Task);
                var branchName = $"bg-{slug}";
                var worktreePath = GitHelper.CreateWorktree(branchName);
                Console.WriteLine($"Created worktree at: {worktreePath}");
                Directory.SetCurrentDirectory(worktreePath);

                var response = await agentPlugin.StartSubtask(settings.Task);
                Console.WriteLine($"Final Response\n\n{response}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        private async Task<string> CreateBranchName(string task)
        {
            var kernel = kf.Create(LLMModel.Small);
            var history = new ChatHistory();
            history.AddSystemMessage(
                """
                Given this task, create a branch naem
                """
            );
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var msg = await chatCompletionService.GetChatMessageContentAsync(history);
            var content = msg.Content ?? Guid.NewGuid().ToString();
            // Simple local slug-generation; fallback to GUID if empty
            var slug = Regex.Replace(content.ToLowerInvariant(), @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"\s+", " ").Trim();
            slug = string.Join('-', slug.Split(' ').Take(6));
            return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N") : slug;
        }

        private static string FormatHistory(ChatHistory history)
        {
            var sb = new StringBuilder();
            // iterate all messages in chronological order
            foreach (var message in history)
            {
                sb.AppendLine($"[{message.Role}] {message.Content}\n----");
            }
            return sb.ToString();
        }
    }
}
