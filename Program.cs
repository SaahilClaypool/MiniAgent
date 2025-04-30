using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;
using System.Text;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (context, services) =>
        {
            services.AddSingleton<KernelFactory>();
            services.AddScoped<WebPlugin>();
            services.AddScoped<DeveloperPlugin>();
            services.AddScoped<AgentPlugin>();
        }
    )
    .Build();
var serviceProvider = host.Services;
var kf = serviceProvider.GetRequiredService<KernelFactory>();
var kernel = kf.Create(
    LLMModel.Large,
    typeof(WebPlugin),
    typeof(DeveloperPlugin),
    typeof(AgentPlugin)
);
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
var history = new ChatHistory();
history.AddSystemMessage(
    """
    You are an agent - please keep going until the users query is completely resolved, before ending your turn and yielding back to the user. Only terminate your turn when you are sure that the problem is solved.
    """
);

AnsiConsole.Clear();

while (true)
{
    var input = AnsiConsole.Prompt(
        new TextPrompt<string>("You > ")
            .PromptStyle("green")
            .Validate(i => string.IsNullOrWhiteSpace(i) ? ValidationResult.Error("Please enter a message.") : ValidationResult.Success())
    );

    if (string.IsNullOrWhiteSpace(input))
        break;

    history.AddUserMessage(input);

    var responseBuilder = new StringBuilder();

    var panel = new Panel(string.Empty)
        .Header("Chat")
        .BorderColor(Color.Grey)
        .Expand();

    await AnsiConsole.Live(panel).StartAsync(async ctx =>
    {
        await foreach (var token in chatCompletionService.GetStreamingChatMessageContentsAsync(
            history,
            kernel: kernel,
            executionSettings: new() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() }
        ))
        {
            responseBuilder.Append(token);

            var renderable = new Rows(
                new Panel(new Markup($"[bold green]You:[/] {input}")) { Border = BoxBorder.None, Padding = new Padding(0, 0, 0, 2) },
                new Panel(new Markup($"[bold yellow]Agent:[/] {responseBuilder}")) { Border = BoxBorder.None, Padding = new Padding(0, 0, 0, 2) }
            );

            var newPanel = new Panel(renderable)
                .Header("Chat")
                .BorderColor(Color.Grey)
                .Expand();

            ctx.UpdateTarget(newPanel);
        }
    });
}

AnsiConsole.MarkupLine("[bold green]Done[/]");
