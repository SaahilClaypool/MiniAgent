using Spectre.Console.Cli;
using System.ComponentModel;

namespace MyAgent.Commands
{
    public class AskSettings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        public string Prompt { get; set; }
    }

    public class AskCommand : Command<AskSettings>
    {
        public override int Execute(CommandContext context, AskSettings settings)
        {
            // You will implement the logic here using the kernel.
            // For now, just echo the prompt.
            Console.WriteLine($"You asked: {settings.Prompt}");
            return 0;
        }
    }
}
