using System;
using System.ComponentModel;
using System.IO;
using MyAgent;
using Spectre.Console.Cli;

namespace MyAgent.Commands
{
    public class WorkInBackgroundSettings : CommandSettings
    {
        [CommandArgument(0, "<task>")]
        public string Task { get; set; } = string.Empty;
    }

    public class WorkInBackgroundCommand : Command<WorkInBackgroundSettings>
    {
        public override int Execute(CommandContext context, WorkInBackgroundSettings settings)
        {
            try
            {
                // Use GitHelper to create a worktree for a branch named based on the task (e.g., "bg-task")
                string branchName = "bg-" + Guid.NewGuid().ToString("N");
                string worktreePath = GitHelper.CreateWorktree(branchName);

                Console.WriteLine($"Created worktree at: {worktreePath}");

                // Change directory to the worktree
                Directory.SetCurrentDirectory(worktreePath);
                Console.WriteLine($"Changed directory to worktree: {worktreePath}");

                // Here you would run the kernel against the task
                // For demonstration, just echo the task
                Console.WriteLine($"Running kernel with task: {settings.Task}");

                // Add actual kernel execution logic here

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
