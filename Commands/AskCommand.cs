using System.ComponentModel;
using Spectre.Console.Cli;

namespace MyAgent.Commands
{
    public class AskSettings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        public string Prompt { get; set; } = string.Empty;
    }

    public class AskCommand(AgentPlugin agentPlugin) : AsyncCommand<AskSettings>
    {
        public override async Task<int> ExecuteAsync(CommandContext context, AskSettings settings)
        {
            var response = await agentPlugin.StartSubtask(settings.Prompt);
            Console.WriteLine($"Final Response\n\n{response}");
            return 0;
        }
    }
}
