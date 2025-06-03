using System.ComponentModel;
using System.IO;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console.Cli;

namespace MyAgent.Commands
{
    public class AskSettings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        public string Prompt { get; set; } = string.Empty;

        [CommandOption("-p|--plan")]
        [Description("Should we pre-plan")]
        public bool? Plan { get; set; }
    }

    public class AskCommand(AgentPlugin agentPlugin, KernelFactory kf) : AsyncCommand<AskSettings>
    {
        public override async Task<int> ExecuteAsync(CommandContext context, AskSettings settings)
        {
            string plan = """
                <plan>
                follow a pattern like this:
                - think_tool: <briefly reframe the components to the problem and what you need to look up>
                - <tool_calls>: look up the information you need whether it be searching the current directory, or the internet. do NOT make up information.
                - think_tool: <summarize what code you need to write AT A HIGH LEVEL>
                - answer the question or perform the task
                </plan>
                """;
            if (settings.Plan == true)
            {
                plan = (await Plan(settings.Prompt)) ?? plan;
            }
            var prompt = $"""
                <request>
                {settings.Prompt}
                </request>

                <plan>
                {plan}
                </plan>

                Now, do the request
                """;
            var response = await agentPlugin.StartSubtask(
                prompt,
                allowSubAgents: settings.Plan == true
            );
            Console.WriteLine($"Final Response\n\n{response}");
            return 0;
        }

        async Task<string?> Plan(string task)
        {
            var kernel = kf.Create(LLMModel.Medium);
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(
                """
                You will be creating a PLAN to do a task.
                You do not have access to any tools.
                When you do the task, you will have access to these tools

                <tools>
                # Available Tools

                ## WebPlugin
                - `WebPlugin_search`: Ask an assistant to search the internet for you. This is not a google search - you should write a longer description of the result you want, and the assistant will search the web and find the results for you. You can also specify what information you want extracted from the output.
                - `WebPlugin_read_page`: Reads the content of a web page given its URL. Set useBrowser to true to use a browser (for dynamic or JavaScript-heavy pages), or false to use a simple HTTP GET request (for static pages). Returns the raw text content of the page. You should USUALLY try to not use the browser. Only use the browser if that fails, or you have an intuition about the site.

                ## DeveloperPlugin
                - `DeveloperPlugin_rg`: Search local files using ripgrep
                - `DeveloperPlugin_think`: Use the tool to think about something. It will not obtain new information or make any changes to the repository, but just log the thought. Use it when complex reasoning or brainstorming is needed.
                - `DeveloperPlugin_read_file`: Read File
                - `DeveloperPlugin_list_files`: list files
                - `DeveloperPlugin_write_file`: Write File
                - `DeveloperPlugin_edit_file`: Edit a file by providing the path, the text to replace, and the replacement text. You should *almost always* use this over `WriteFile` to avoid overwriting the entire file. Each searchText should be a contiguous chunk of lines to search for in the existing source code. This search text much match EXACTLY with the text in the file. You will replace ALL of the searchText with the new text. Make sure you search for ALL of the text you need to replace.
                - `DeveloperPlugin_run_cli_command`: Runs a CLI command after confirming with the user. If 'n' or 'no', denies. If 'y' or 'yes', runs.

                ## AgentPlugin
                - `AgentPlugin_start_subtask`: Ask an agent to run a subtask for you. Give it a *detailed* and *specific* prompt of what you want it to do. Whenever you have a large problem, use this agent to do sub tasks so you can stay focused on the big picture. You should always make a plan for how thes subtasks will accomplish your main task before making them.
                - `AgentPlugin_use_an_expert`: Ask an expert agent a question. Make sure you provide a detailed task definition including the output you need. Make sure to provide as much context as you can. Use this in the following circumstances: - When you need to do a complex task, ask for a plan of action for how you will solve it - When you need to solve a complicated problems (algorithms, coding etc.) ask this agent to help you solve it
                </tools>


                Now, you should MAKE A PLAN in the form of a bulleted list of the best way to solve the problem:

                Example:

                <task>
                Change all the usages of the find text function and make them async
                </task>

                <plan>
                - List the files to find out what project structure we are working with
                - Use the rg tool to find the function definition; search for things like "FindText" or "find_text" or rg -i -e 'find[\s_\-]*text'. We can search multiple tool calls at once.
                - Look at the full function definition once we find it using the read file tool
                - Use the think tool to think through how we will refactor it
                - edit_file to edit the function
                - Find all references
                - Use the think_tool to list all usages
                - Make a sub-task to edit each usage to be async. Give each subtask enough information to do the problem without further research.
                </plan>

                <task>
                Find all the ice cream places in Waltham MA, and compare their prices
                </task>

                <plan>
                - Start a subtask to compile a list of all ice cream places. Wait for the result
                - For each result, start a subtask for "find menu prices" - wait for the results
                - Once I have all the prices per location, create a table of prices per place.
                </plan>

                """
            );
            history.AddUserMessage($"Create a plan for this task: {task}");

            var plan = await chatCompletionService.GetChatMessageContentAsync(history);
            return plan.Content;
        }
    }
}
