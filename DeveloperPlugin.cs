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

    [KernelFunction]
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

    [KernelFunction]
    [Description("Search local files using ripgrep")]
    public async Task<string> Rg(string search)
    {
        _logger.LogInformation($"Searching for '{search}' using ripgrep...");
        var rgPath = "rg";
        var arguments = $"\"{search}\" -C 2 --max-columns 200 ";

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
            throw new Exception($"Ripgrep failed with exit code {process.ExitCode}: {error}");
        }
        _logger.LogInformation($"Ripgrep results:\n{output}");

        return output;
    }

    [KernelFunction]
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

    [KernelFunction]
    [Description("Read File")]
    public string ReadFile(string path)
    {
        _logger.LogTrace($"Reading file: {path}");
        if (!File.Exists(path))
        {
            _logger.LogError($"File not found: {path}");
            throw new FileNotFoundException($"File not found: {path}");
        }
        var content = File.ReadAllText(path);
        _logger.LogTrace(
            $"Read file content (first 100 chars): {content.Substring(0, Math.Min(content.Length, 100))}"
        );
        return content;
    }

    [KernelFunction]
    [Description("list files")]
    public string ListFiles(string path)
    {
        _logger.LogTrace($"Listing files in directory: {path}");
        if (!Directory.Exists(path))
        {
            _logger.LogError($"Directory not found: {path}");
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
        var files = string.Join(" ", Directory.GetFiles(path));
        var directories = string.Join(" ", Directory.GetDirectories(path));
        _logger.LogTrace($"Files in {path}: {files}");
        _logger.LogTrace($"Directories in {path}: {directories}");

        return $"Files: {files} Directories: {directories}";
    }

    [KernelFunction]
    [Description("Write File")]
    public string WriteFile(string path, string content)
    {
        _logger.LogTrace($"Writing file: {path}");
        File.WriteAllText(path, content);
        _logger.LogTrace($"Finished writing file: {path}");
        return $"wrote content to {path}";
    }

    [KernelFunction]
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
            // fall back: find best matching line
            var lines = content.Split('\n');
            var best = lines
                .Select(
                    (line, idx) =>
                        new
                        {
                            Line = line,
                            Distance = EditDistance(line.Trim(), searchText.Trim()),
                            Index = idx
                        }
                )
                .OrderBy(x => x.Distance)
                .First();

            // threshold = half the length of the line we're comparing
            if (best.Distance > best.Line.Length / 2)
            {
                _logger.LogError(
                    $"Text to replace not found. Closest match (distance {best.Distance}): {best.Line}"
                );
                throw new ArgumentException(
                    $"Text to replace not found. Closest match (distance {best.Distance}): {best.Line}"
                );
            }

            // replace that single line
            lines[best.Index] = replacement;
            content = string.Join('\n', lines);
            _logger.LogTrace($"Replaced line {best.Index} in {path} with '{replacement}'");
        }

        File.WriteAllText(path, content);
        _logger.LogTrace($"Finished editing file: {path}");
        return $"Wrote edits to {path}";
    }

    // [KernelFunction]
    [Description(
        "Runs a CLI command after confirming with the user. If 'n' or 'no', denies. If 'y' or 'yes', runs."
    )]
    public async Task<string> RunCliCommand(string command)
    {
        _logger.LogInformation(
            $"Proposed CLI command: '{command}'. Waiting for user confirmation."
        );
        Console.WriteLine($"Confirm running command '{command}'? (y/n)");
        var confirmation = Console.ReadLine()?.ToLowerInvariant();

        if (confirmation == "y" || confirmation == "yes")
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
                    return $"Command executed successfully. Output: {output}";
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
        else if (confirmation == "n" || confirmation == "no")
        {
            _logger.LogInformation("User denied command execution.");
            return "Command execution denied by user.";
        }
        else
        {
            _logger.LogInformation(
                "Invalid confirmation input. Command execution denied by default."
            );
            return "Invalid confirmation input. Command execution denied.";
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
