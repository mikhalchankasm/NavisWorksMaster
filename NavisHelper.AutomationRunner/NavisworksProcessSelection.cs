using System;
using System.Collections.Generic;
using System.Text;

namespace NavisHelper.AutomationRunner
{
    public sealed class NavisworksProcessInfo
    {
        public int ProcessId;
        public string ImageName;
        public string CommandLine;
    }

    public static class NavisworksProcessSelection
    {
        public static IList<int> SelectNew(ICollection<int> existingBefore,
            IEnumerable<NavisworksProcessInfo> current, ICollection<int> alreadyJoined)
        {
            var selected = new List<int>();
            foreach (var process in current)
            {
                if (!string.Equals(process.ImageName, "Roamer.exe", StringComparison.OrdinalIgnoreCase) ||
                    process.CommandLine == null || existingBefore.Contains(process.ProcessId) ||
                    alreadyJoined.Contains(process.ProcessId))
                    continue;
                bool executable = true;
                foreach (string argument in SplitArguments(process.CommandLine))
                {
                    if (executable)
                    {
                        executable = false;
                        continue;
                    }
                    if (string.Equals(argument, "-Embedding", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(argument, "/Embedding", StringComparison.OrdinalIgnoreCase))
                    {
                        selected.Add(process.ProcessId);
                        break;
                    }
                }
            }
            return selected;
        }

        private static IEnumerable<string> SplitArguments(string commandLine)
        {
            var argument = new StringBuilder();
            bool quoted = false;
            bool started = false;
            foreach (char character in commandLine)
            {
                if (character == '"')
                {
                    quoted = !quoted;
                    started = true;
                }
                else if (!quoted && char.IsWhiteSpace(character))
                {
                    if (started)
                    {
                        yield return argument.ToString();
                        argument.Clear();
                        started = false;
                    }
                }
                else
                {
                    argument.Append(character);
                    started = true;
                }
            }
            if (started)
                yield return argument.ToString();
        }
    }
}
