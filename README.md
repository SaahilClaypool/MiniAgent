# MyAgent

## Description

This project appears to be a .NET-based AI agent with various plugins to extend its capabilities.

## Features

- **AgentPlugin:** Core plugin for agent functionalities.
- **StatePlugin:** Manages the state of tasks, including task completion.
- **DeveloperPlugin:** Provides tools for interacting with the development environment, such as reading/writing files, searching, and getting repository overview.
- **WebPlugin:** Enables searching the internet.
- **AskCommand:** Command for asking the agent questions.
- **ChatCommand:** Command for general chat interactions.
- **WorkInBackgroundCommand:** Command for running tasks in the background.

## Getting Started

To get started with this project, you will need the .NET SDK installed.

1. Clone the repository.
2. Navigate to the project directory.
3. Build the project using `dotnet build`.
4. Run the project using `dotnet run`.

Further configuration or specific instructions might be required depending on the intended use case.

## Environment Variables

The following environment variables are used by this application:

- `Chat:Endpoint`: The endpoint URL for the chat service.
- `Chat:ApiKey`: The API key for authenticating with the chat service.
- `Chat:LargeModel`: The name or identifier of the large chat model to use.
- `Chat:SmallModel`: The name or identifier of the small chat model to use.
- `Chat:SearchModel`: The name or identifier of the chat model to use for search operations.