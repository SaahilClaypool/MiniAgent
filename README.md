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

- `AG_Chat:Endpoint`: The endpoint URL for the chat service.
- `AG_Chat:ApiKey`: The API key for authenticating with the chat service.
- `AG_Chat:LargeModel`: The name or identifier of the large chat model to use.
- `AG_Chat:SmallModel`: The name or identifier of the small chat model to use.
- `AG_Chat:SearchModel`: The name or identifier of the chat model to use for search operations.

## `edit_file` format for LLMs

The `edit_file` function is designed for precise file modifications. It requires the following parameters:

- `path`: The path to the file to be edited.
- `editLineStart`: The exact string of the line where the replacement or insertion should begin.
- `editLineEnd`: The exact string of the line where the replacement should end.
- `replacement`: The new text that will replace the content between `editLineStart` and `editLineEnd`.

**How it works:**

The function identifies the block of text between `editLineStart` and `editLineEnd` (inclusive) and replaces it entirely with the `replacement` string.

**Use cases:**

- **Replacing existing content:** Provide `editLineStart` and `editLineEnd` that define the block to be replaced, and the new content in `replacement`.
- **Inserting text:**
  - To insert text **after** `editLineStart`, set `editLineEnd` to an empty string `""`.
  - To insert text **at the beginning of the file**, set `editLineStart` to an empty string `""`. In this case, `editLineEnd` will be the first line of the original file.

**Example:**

To change:

```
Hello,
This is old text.
Goodbye.
```

to:

```
Hello,
This is new text.
Goodbye.
```

You would use:
`editLineStart="This is old text."`
`editLineEnd="This is old text."`
`replacement="This is new text."`

To insert "New line." after "Hello,":

You would use:
`editLineStart="Hello,"`
`editLineEnd=""`
`replacement="Hello,
New line."`
