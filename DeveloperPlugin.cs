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
            Each searchText should be a contiguous chunk of lines to search for in the existing source code.
            You will replace ALL of the searchText with the new text.
            Make sure you search for ALL of the text you need to replace.
            """
    )]
    public string EditFile(string path, string searchText, string replacement)
    {
        _logger.LogTrace($"Editing file: {path} replacing '{searchText}' with '{replacement}'");
        if (!File.Exists(path))
        {
            _logger.LogError($"File not found: {path}");
            throw new FileNotFoundException($"File not found: {path}");
        }

        var content = File.ReadAllText(path);

        if (content.Contains(searchText))
        {
            // replace only the first occurrence
            bool replaced = false;
            content = Regex.Replace(
                content,
                Regex.Escape(searchText),
                m =>
                {
                    if (!replaced)
                    {
                        replaced = true;
                        return replacement;
                    }
                    return m.Value;
                },
                RegexOptions.None,
                TimeSpan.FromSeconds(1)
            );
            _logger.LogTrace($"Replaced first occurrence of '{searchText}' in {path}");
        }
        else
        {
            // Split the searchText and file content into lines
            var searchLines = searchText.Split('\n');
            var fileLines = content.Split('\n');
            
            // Can't find chunks if search has more lines than the file
            if (searchLines.Length > fileLines.Length)
            {
                _logger.LogError($"Search text has more lines ({searchLines.Length}) than the file ({fileLines.Length})");
                throw new ArgumentException($"Search text has more lines ({searchLines.Length}) than the file ({fileLines.Length})");
            }
            
            // Find the chunk of lines that best matches the search text
            var bestDistance = int.MaxValue;
            var bestStartIndex = 0;
            
            // For each possible starting position in the file
            for (int i = 0; i <= fileLines.Length - searchLines.Length; i++)
            {
                // Calculate the combined distance for this chunk
                int chunkDistance = 0;
                for (int j = 0; j < searchLines.Length; j++)
                {
                    chunkDistance += EditDistance(fileLines[i + j].Trim(), searchLines[j].Trim());
                }
                
                if (chunkDistance < bestDistance)
                {
                    bestDistance = chunkDistance;
                    bestStartIndex = i;
                }
            }
            
            // Calculate a threshold based on the total characters in the search text
            int totalSearchLength = searchLines.Sum(line => line.Length);
            int threshold = Math.Max(totalSearchLength / 2, 10); // At least 10 chars difference
            
            // Get the best matching chunk for logging
            var bestChunk = string.Join("\n", fileLines.Skip(bestStartIndex).Take(searchLines.Length));
            
            if (bestDistance > threshold)
            {
                _logger.LogError(
                    $"Text to replace not found. Closest match (distance {bestDistance}): {bestChunk}"
                );
                throw new ArgumentException(
                    $"Text to replace not found. Closest match (distance {bestDistance}): {bestChunk}"
                );
            }
            
            // Replace the chunk with the replacement text
            var beforeChunk = fileLines.Take(bestStartIndex).ToList();
            var afterChunk = fileLines.Skip(bestStartIndex + searchLines.Length).ToList();
            var replacementLines = replacement.Split('\n');
            
            var newLines = beforeChunk.Concat(replacementLines).Concat(afterChunk).ToArray();
            content = string.Join('\n', newLines);
            
            _logger.LogTrace($"Replaced lines {bestStartIndex}-{bestStartIndex + searchLines.Length - 1} in {path} with replacement text");
        }

        File.WriteAllText(path, content);
        _logger.LogTrace($"Finished editing file: {path}");
        return $"Wrote edits to {path}";
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
