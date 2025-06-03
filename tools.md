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

## StatePlugin
- `StatePlugin_complete`: Call this to mark the task as completed
