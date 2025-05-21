using System;
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

public class KernelFactory
{
    private readonly IConfiguration config;
    private readonly IServiceProvider services;
    static readonly ConcurrentDictionary<string, HttpClient> httpClients = new();

    public KernelFactory(IConfiguration config, IServiceProvider services)
    {
        this.config = config;
        this.services = services;
    }

    public Kernel Create(LLMModel model, params Type[] plugins)
    {
        var (endpoint, apiKey, largeModel, smallModel, searchModel) = (
            config["Chat:Endpoint"] ?? throw new ArgumentException("Chat:Endpoint"),
            config["Chat:ApiKey"] ?? throw new ArgumentException("Chat:ApiKey"),
            config["Chat:LargeModel"] ?? throw new ArgumentException("Chat:LargeModel"),
            config["Chat:SmallModel"] ?? throw new ArgumentException("Chat:SmallModel"),
            config["Chat:SearchModel"] ?? throw new ArgumentException("Chat:SearchModel")
        );
        var builder = Kernel.CreateBuilder();
        var chatClient = httpClients.GetOrAdd(
            endpoint,
            (_) =>
            {
                var client = new HttpClient();
                client.BaseAddress = new Uri(endpoint);
                client.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost");
                client.DefaultRequestHeaders.Add("X-Title", "My Semantic Kernel App");
                return client;
            }
        );
        switch (model)
        {
            case LLMModel.Large:
                builder.AddOpenAIChatCompletion(largeModel, apiKey: apiKey, httpClient: chatClient);
                break;
            case LLMModel.Small:
                builder.AddOpenAIChatCompletion(smallModel, apiKey: apiKey, httpClient: chatClient);
                break;
            case LLMModel.Search:
                builder.AddOpenAIChatCompletion(
                    searchModel,
                    apiKey: apiKey,
                    httpClient: chatClient
                );
                break;
        }
        builder.Services.AddSingleton(services.GetRequiredService<ILoggerFactory>());
        builder.Services.AddScoped<KernelFactory>();
        foreach (var type in plugins)
        {
            var plugin =
                services.GetService(type)
                ?? ActivatorUtilities.CreateInstance(services, type)
                ?? throw new ArgumentException($"Failed to create service: {type.Name}");
            builder.Plugins.AddFromObject(plugin);
        }
        var kernel = builder.Build();
        kernel.FunctionInvocationFilters.Add(new LoggingFilter(services.GetRequiredService<ILogger<Kernel>>()));
        return kernel;
    }
}

public class LoggingFilter(ILogger logger) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        Console.WriteLine($"Calling {context.Function} with {context.Arguments}");
        await next(context);
    }
}