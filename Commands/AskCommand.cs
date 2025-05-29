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
            var prompt = $"""
            Users's Request: {settings.Prompt}

            If this is a coding question, you should probably follow a pattern like this:
            - think_tool: <briefly reframe the components to the problem and what you need to look up>
            - rg_tool: as needed, find the relevant code samples. do NOT just make up code
            - think_tool: <summarize what code you need to write AT A HIGH LEVEL>
            - ... do the task
            """;
            var response = await agentPlugin.StartSubtask(prompt);
            Console.WriteLine($"Final Response\n\n{response}");
            return 0;
        }
    }
}
