# Project Summary: Chat Agent with Plugin Architecture

## Overview
This project is a console-based chat agent built using the Microsoft Semantic Kernel. It supports interactive streaming chat where the agent processes user inputs until the query is fully resolved. The solution integrates multiple plugins providing key capabilities such as web interaction, codebase exploration, and task delegation.

## Key Concepts

### Kernel and Plugins
- **KernelFactory**: Creates instances of the kernel with specified plugins.
- **Plugins**: Extend the kernel capabilities with additional functions.
  - `WebPlugin`: (Details not provided in current files)
  - `DeveloperPlugin`: Provides repository introspection, file operations, and shell tool integration (like ripgrep) for searching code.
  - `AgentPlugin`: Enables recursive task delegation by running subtasks within the agent itself for complex problem-solving.

### Chat Interaction
- Interactive chat UI rendered in the console using Spectre.Console.
- Maintains a chat history of system, user, and agent messages.
- Streams tokenized output from the chat completion service in real-time.
- System instructions guide the agent to keep resolving queries fully before ending interaction.

## Where to Find Key Information

- **Program.cs**: Application entry point, sets up the host, dependency injection, kernel instantiation, and chat loop.
- **AgentPlugin.cs**: Contains the `AgentPlugin` class which manages delegation of subtasks through recursive calls.
- **DeveloperPlugin.cs**: Contains developer tooling for listing files, reading, writing, editing files, and running repository searches.

## Summary
This project demonstrates a modular approach to building a conversational AI agent with direct integration to developer tools and recursive task handling to break down complex queries. The codebase is designed to be extensible with additional plugins and services.

---

For further exploration, consult the `.cs` source files and look for functions decorated with `[KernelFunction]` which expose capabilities to the kernel.
