using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

public class DeveloperPlugin
{
    private readonly ILogger<DeveloperPlugin> _logger;

    public DeveloperPlugin(ILogger<DeveloperPlugin> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// TODO: make this configurable
    /// </summary>
    async Task<bool> Confirm(string message)
    {
        Console.WriteLine(message);
        var inputTask = Task.Run(() => Console.ReadLine());
        var completedTask = await Task.WhenAny(inputTask, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completedTask == inputTask)
        {
            var input = inputTask.Result;
            return input?.StartsWith("y", StringComparison.CurrentCultureIgnoreCase) == true;
        }
        else
        {
            // Timeout: assume confirmation
            _logger.LogInformation(
                "No input received within 10 seconds. Assuming confirmation (yes)."
            );
            return true;
        }
    }

    // [KernelFunction("repository_overview")]
    [Description("Get repository symbol overview")]
    public async Task<string> RepositoryOverview()
    {
        _logger.LogInformation("Getting repository overview...");
        var psi = new ProcessStartInfo("aider", "--show-repo-map")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        var proc = Process.Start(psi);
        var repoMap = await proc!.StandardOutput.ReadToEndAsync();
        _logger.LogInformation($"Repository overview:\n{repoMap}");
        return repoMap;
    }

    [KernelFunction("rg")]
    [Description("Search local files using ripgrep")]
    public async Task<string> Rg(string search)
    {
        _logger.LogInformation($"Searching for '{search}' using ripgrep...");
        var rgPath = "rg";
        var arguments = $"\"{search}\" -C 2 --max-columns 200 -H -N";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = rgPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError($"Ripgrep failed with exit code {process.ExitCode}: {error}");
            return $"Error: Ripgrep failed with exit code {process.ExitCode}: {error}";
        }
        _logger.LogInformation($"Ripgrep results:\n{output}");

        return output;
    }

    [KernelFunction("think")]
    [Description(
        """
            Use the tool to think about something. It will not obtain new information or make any changes to the repository, but just log the thought. Use it when complex reasoning or brainstorming is needed. 

            Common use cases:
            1. When exploring a repository and discovering the source of a bug, call this tool to brainstorm several unique ways of fixing the bug, and assess which change(s) are likely to be simplest and most effective
            2. After receiving test results, use this tool to brainstorm ways to fix failing tests
            3. When planning a complex refactoring, use this tool to outline different approaches and their tradeoffs
            4. When designing a new feature, use this tool to think through architecture decisions and implementation details
            5. When debugging a complex issue, use this tool to organize your thoughts and hypotheses

            The tool simply logs your thought process for better transparency and does not execute any code or make changes.
            """
    )]
    public string Think(string thought)
    {
        _logger.LogInformation($"Thinking: {thought}");
        return $"Your thought has been logged";
    }

    [KernelFunction("read_file")]
    [Description("Read File")]
    public string ReadFile(string path)
    {
        _logger.LogTrace($"Reading file: {path}");
        if (!File.Exists(path))
        {
            _logger.LogError($"File not found: {path}");
            return $"Error: File not found: {path}";
        }
        var content = File.ReadAllText(path);
        _logger.LogTrace(
            $"Read file content (first 100 chars): {content.Substring(0, Math.Min(content.Length, 100))}"
        );
        return content;
    }

    [KernelFunction("list_files")]
    [Description("list files")]
    public string ListFiles(string path)
    {
        _logger.LogTrace($"Listing files in directory: {path}");
        if (!Directory.Exists(path))
        {
            _logger.LogError($"Directory not found: {path}");
            return $"Error: Directory not found: {path}";
        }
        var files = string.Join(" ", Directory.GetFiles(path));
        var directories = string.Join(" ", Directory.GetDirectories(path));
        _logger.LogTrace($"Files in {path}: {files}");
        _logger.LogTrace($"Directories in {path}: {directories}");

        return $"Files: {files} Directories: {directories}";
    }

    [KernelFunction("write_file")]
    [Description("Write File")]
    public string WriteFile(string path, string content)
    {
        _logger.LogTrace($"Writing file: {path}");
        File.WriteAllText(path, content);
        _logger.LogTrace($"Finished writing file: {path}");
        return $"wrote content to {path}";
    }

    [KernelFunction("edit_file")]
    [Description(
        """
            Edit a file by providing the path, the text to replace, and the replacement text.
            You should *almost always* use this over `WriteFile` to avoid overwriting the entire file.

            You will pass in the edit line start and edit line end.
            These must be EXACT matches for text in the file. All of the lines from editLineStart to editLineEnd will be replaced with the replacement.
            To insert text, editLineEnd should be set to an empty string "".
            To insert text at the START of the file, set editLineStart to an empty string "".

            Example:
            To replace "old_text" with "new_text" in a file named "example.txt":
            edit_file(path="example.txt", editLineStart="old_text_start", editLineEnd="old_text_end", replacement="new_text")
            """
    )]
    public string EditFile(
        string path,
        string editLineStart,
        string editLineEnd,
        string replacement
    )
    {
        _logger.LogTrace(
            $"Editing file: {path} replacing '{editLineStart}' to '{editLineEnd}' with '{replacement}'"
        );
        if (!File.Exists(path))
        {
            _logger.LogError($"File not found: {path}");
            throw new FileNotFoundException($"File not found: {path}");
        }

        var content = File.ReadAllText(path);
        var lines = content.Split('\n');
        int startIndex = -1,
            endIndex = -1;

        if (!string.IsNullOrEmpty(editLineStart))
        {
            startIndex = FindLineIndex(lines, editLineStart, "start", _logger);
            if (startIndex == -1)
                return $"Error: Failed to find startIndex: {editLineStart}";
        }
        if (!string.IsNullOrEmpty(editLineEnd))
        {
            endIndex = FindLineIndex(lines, editLineEnd, "end", _logger);
            if (endIndex == -1)
                return $"Error: Failed to find endIndex: {editLineEnd}";
        }

        string newContent;
        if (string.IsNullOrEmpty(editLineStart) && string.IsNullOrEmpty(editLineEnd))
        {
            // Insert at start of file
            newContent = string.Join('\n', new[] { replacement }.Concat(lines));
        }
        else if (!string.IsNullOrEmpty(editLineStart) && string.IsNullOrEmpty(editLineEnd))
        {
            // Insert after start line
            newContent = string.Join(
                '\n',
                lines
                    .Take(startIndex + 1)
                    .Concat(new[] { replacement })
                    .Concat(lines.Skip(startIndex + 1))
            );
        }
        else if (!string.IsNullOrEmpty(editLineStart) && !string.IsNullOrEmpty(editLineEnd))
        {
            // Replace from startIndex to endIndex (inclusive)
            if (endIndex < startIndex)
                return $"Error: endIndex ({endIndex}) must be >= startIndex ({startIndex})";
            newContent = string.Join(
                '\n',
                lines
                    .Take(startIndex)
                    .Concat(new[] { replacement })
                    .Concat(lines.Skip(endIndex + 1))
            );
        }
        else if (string.IsNullOrEmpty(editLineStart) && !string.IsNullOrEmpty(editLineEnd))
        {
            // Insert before end line
            newContent = string.Join(
                '\n',
                lines.Take(endIndex).Concat(new[] { replacement }).Concat(lines.Skip(endIndex))
            );
        }
        else
        {
            throw new ArgumentException(
                "Invalid edit parameters: Must specify either editLineStart, editLineEnd, or both."
            );
        }

        File.WriteAllText(path, newContent);
        _logger.LogTrace($"Finished editing file: {path}");
        return $"Wrote edits to {path}";
    }

    private int FindLineIndex(string[] lines, string targetLine, string searchType, ILogger logger)
    {
        // Exact match first
        int index = Array.FindIndex(lines, l => l.Trim() == targetLine.Trim());
        if (index != -1)
        {
            logger.LogTrace($"Found {searchType} index by exact match: {index}");
            return index;
        }

        logger.LogTrace(
            $"Exact match not found for {searchType}, attempting fuzzy match for: {targetLine}"
        );
        int minDistance = int.MaxValue;
        int bestIndex = -1;
        const int FUZZY_MATCH_THRESHOLD = 5; // Define a reasonable threshold

        for (int i = 0; i < lines.Length; i++)
        {
            int distance = EditDistance(targetLine.Trim(), lines[i].Trim());
            if (distance < minDistance)
            {
                minDistance = distance;
                bestIndex = i;
            }
        }

        if (bestIndex != -1 && minDistance <= FUZZY_MATCH_THRESHOLD)
        {
            logger.LogTrace(
                $"Found {searchType} index by fuzzy match (distance {minDistance}): {bestIndex}"
            );
            return bestIndex;
        }
        logger.LogWarning(
            $"Fuzzy match found for {searchType} with distance {minDistance} did not meet threshold {FUZZY_MATCH_THRESHOLD}. Target: '{targetLine}' Closest: '{lines[bestIndex]}'"
        );

        logger.LogWarning($"Could not find {searchType} index for: {targetLine}");
        return -1; // Not found
    }

    [KernelFunction("run_cli_command")]
    [Description(
        "Runs a CLI command after confirming with the user. If 'n' or 'no', denies. If 'y' or 'yes', runs."
    )]
    public async Task<string> RunCliCommand(string command)
    {
        _logger.LogInformation(
            $"Proposed CLI command: '{command}'. Waiting for user confirmation."
        );
        var confirmation = await Confirm($"Would you like to run {command}");

        if (confirmation)
        {
            _logger.LogInformation($"User confirmed. Running command: '{command}'");
            try
            {
                var psi = new ProcessStartInfo()
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (
                    System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.Windows
                    )
                )
                {
                    psi.FileName = "cmd.exe";
                    psi.Arguments = $"/c \"{command}\"";
                }
                else if (
                    System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.Linux
                    )
                    || System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.OSX
                    )
                )
                {
                    psi.FileName = "/bin/sh"; // or sh
                    psi.Arguments = $"-c \"{command}\"";
                }
                else
                {
                    // Fallback or throw an exception for unsupported OS
                    _logger.LogError("Unsupported operating system.");
                    return "Error: Unsupported operating system.";
                }
                using var process = Process.Start(psi);
                var output = await process!.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation($"Command executed successfully. Output: {output}");
                    return $"Command executed successfully. Output:\n {output}";
                }
                else
                {
                    _logger.LogError(
                        $"Command failed with exit code {process.ExitCode}. Error: {error}"
                    );
                    return $"Command failed with exit code {process.ExitCode}. Error: {error} Output: {output}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error executing command: {ex.Message}");
                return $"Error executing command: {ex.Message}";
            }
        }
        else
        {
            _logger.LogInformation("User denied command execution.");
            return "Command execution denied by user.";
        }
    }

    public static int EditDistance(string s, string t)
    {
        var d = new int[s.Length + 1, t.Length + 1];
        for (var i = 0; i <= s.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++)
            d[0, j] = j;

        for (var i = 1; i <= s.Length; i++)
        {
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(
                        d[i - 1, j] + 1, // deletion
                        d[i, j - 1] + 1
                    ), // insertion
                    d[i - 1, j - 1] + cost // substitution
                );
            }
        }

        return d[s.Length, t.Length];
    }
}
