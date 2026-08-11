using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VibeDeck.Host.Quotas
{
    /// <summary>
    /// Terminates running CLI processes before an account switch.
    /// Codex only reads a single ~/.codex/auth.json; a running session's token
    /// refresh will clobber a freshly-swapped account, so the old process MUST be
    /// killed before switching (confirmed behavior, not just UX polish).
    /// </summary>
    internal static class CliProcessManager
    {
        // Process names (without .exe) to terminate per provider family.
        // Never terminate the ChatGPT desktop app: VibeDeck only manages the Codex CLI.
        internal static readonly string[] CodexProcessNames = { "codex" };

        /// <summary>
        /// Kills every running process matching any of the given names (case-insensitive,
        /// no extension) along with its child process tree. Returns the number killed.
        /// Never throws: a process that exits or is inaccessible mid-scan is skipped.
        /// </summary>
        internal static int KillByNames(IEnumerable<string> processNames)
        {
            if (processNames == null)
            {
                return 0;
            }

            var distinct = processNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var killed = 0;
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            foreach (var name in distinct)
            {
                Process[] matches;
                try
                {
                    matches = Process.GetProcessesByName(name);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                foreach (var process in matches)
                {
                    try
                    {
                        if (process.SessionId != currentSessionId)
                        {
                            continue;
                        }

                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                        killed++;
                    }
                    catch (Exception ex) when (
                        ex is InvalidOperationException ||   // already exited
                        ex is System.ComponentModel.Win32Exception || // access denied / cannot terminate
                        ex is NotSupportedException)
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return killed;
        }
    }
}
