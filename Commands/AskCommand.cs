using System.ComponentModel;
using Spectre.Console.Cli;

namespace MyAgent.Commands
{
    public class AskSettings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        public string Prompt { get; set; } = string.Empty;

        [CommandOption("--planner")]
        [Description("Enables planner mode, where the agent creates and follows a plan.")]
        public bool PlannerMode { get; set; } = false;
    }

    public class AskCommand(AgentPlugin agentPlugin) : AsyncCommand<AskSettings>
    {
        public override async Task<int> ExecuteAsync(CommandContext context, AskSettings settings)
        {
            string response;
            if (settings.PlannerMode)
            {
                response = await agentPlugin.StartPlannerTask(settings.Prompt);
            }
            else
            {
                response = await agentPlugin.StartSubtask(settings.Prompt);
            }
            Console.WriteLine($"Final Response\n\n{response}");
            return 0;
        }
    }
}
