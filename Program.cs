using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyAgent.Commands;
using Spectre.Console.Cli;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (context, services) =>
        {
            services.AddSingleton<KernelFactory>();
            services.AddScoped<WebPlugin>();
            services.AddScoped<DeveloperPlugin>();
            services.AddScoped<AgentPlugin>();
            services.AddScoped<AskCommand>(); // Register the command
        }
    )
    .Build();

var app = new CommandApp();
app.Configure(config =>
{
    config
        .AddCommand<AskCommand>("ask")
        .WithDescription("Process a user query using the agent.")
        .WithExample(new[] { "ask", "What is the answer?" });

    config
        .AddCommand<ChatCommand>("chat")
        .WithDescription("Start interactive agent chat session.");
});

await app.RunAsync(args);
