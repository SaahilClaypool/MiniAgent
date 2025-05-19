using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyAgent.Commands;
using Spectre.Console.Cli;

var config = new ConfigurationBuilder();
config.AddEnvironmentVariables();
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IConfiguration>(config.Build());
services.AddSingleton<KernelFactory>();
services.AddScoped<WebPlugin>();
services.AddScoped<DeveloperPlugin>();
services.AddScoped<AgentPlugin>();
services.AddScoped<AskCommand>(); // Register the command
services.AddScoped<WorkInBackgroundCommand>(); // Register the new WorkInBackgroundCommand
services.AddScoped<ChatCommand>(); // << Register the new ChatCommand
var registrar = new MyTypeRegistrar(services);

var app = new CommandApp(registrar);
app.Configure(config =>
{
    config.PropagateExceptions();

    config
        .AddCommand<AskCommand>("ask")
        .WithDescription("Process a user query using the agent.")
        .WithExample(new[] { "ask", "What is the answer?" });

    config.AddCommand<ChatCommand>("chat").WithDescription("Start interactive agent chat session.");
    config.AddCommand<WorkInBackgroundCommand>("bg").WithDescription("Start a task in the background");
});

await app.RunAsync(args);

public sealed class MyTypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _builder;

    public MyTypeRegistrar(IServiceCollection builder)
    {
        _builder = builder;
    }

    public ITypeResolver Build()
    {
        return new MyTypeResolver(_builder.BuildServiceProvider());
    }

    public void Register(Type service, Type implementation)
    {
        _builder.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _builder.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> func)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        _builder.AddSingleton(service, (provider) => func());
    }
}

public sealed class MyTypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public MyTypeResolver(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public object Resolve(Type? type)
    {
        if (type == null)
        {
            return null!;
        }

        return _provider.GetService(type)!;
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
