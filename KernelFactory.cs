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

    public Kernel Create(LLMModel model, params IEnumerable<Type> plugins)
    {
        var (endpoint, apiKey, largeModel, mediumModel, smallModel, searchModel) = (
            config["AG_Chat:Endpoint"] ?? "https://openrouter.ai/api/v1",
            config["AG_Chat:ApiKey"] ?? throw new ArgumentException("AG_Chat:ApiKey"),
            config["AG_Chat:LargeModel"] ?? "openai/o4-mini",
            config["AG_Chat:MediumModel"] ?? "google/gemini-2.5-flash-preview-05-20",
            config["AG_Chat:SmallModel"] ?? "google/gemini-2.5-flash-preview-05-20",
            config["AG_Chat:SearchModel"] ?? "perplexity/sonar"
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
            case LLMModel.Medium:
                builder.AddOpenAIChatCompletion(
                    mediumModel,
                    apiKey: apiKey,
                    httpClient: chatClient
                );
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
        kernel.FunctionInvocationFilters.Add(
            new LoggingFilter(services.GetRequiredService<ILogger<Kernel>>())
        );
        return kernel;
    }
}

public class LoggingFilter(ILogger logger) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next
    )
    {
        await next(context);
    }
}
