using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RawX
{
    public class Preprocessor
    {
        private string rootPath;
        private TargetPlatform target;
        private HashSet<string> definedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Preprocessor(string rootFilePath, TargetPlatform platform)
        {
            rootPath = Path.GetDirectoryName(Path.GetFullPath(rootFilePath));
            target = platform;

            // Set platform symbols
            switch (target)
            {
                case TargetPlatform.Windows:
                    definedSymbols.Add("__WINDOWS__");
                    definedSymbols.Add("_WIN64");
                    definedSymbols.Add("TARGET_WINDOWS");
                    definedSymbols.Add("OS_WINDOWS");
                    break;
                case TargetPlatform.Linux:
                    definedSymbols.Add("__LINUX__");
                    definedSymbols.Add("_LINUX64");
                    definedSymbols.Add("TARGET_LINUX");
                    definedSymbols.Add("OS_LINUX");
                    break;
                case TargetPlatform.MacOS:
                    definedSymbols.Add("__MACOS__");
                    definedSymbols.Add("__DARWIN__");
                    definedSymbols.Add("_MACOS64");
                    definedSymbols.Add("TARGET_MACOS");
                    definedSymbols.Add("OS_MACOS");
                    break;
            }

            definedSymbols.Add("RAWX");
            definedSymbols.Add("ARCH_AMD64");
            definedSymbols.Add("ARCH_X86_64");
            definedSymbols.Add("RAWX_VERSION_2");
        }

        public void Define(string symbol, string value = "")
        {
            definedSymbols.Add(symbol);
            if (!string.IsNullOrEmpty(value))
            {
                defines[symbol] = value;
            }
        }

        public string Process(string source, string currentFile = null)
        {
            if (currentFile != null)
            {
                includedFiles.Add(Path.GetFullPath(currentFile));
            }

            StringBuilder output = new StringBuilder();
            string[] lines = source.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            Stack<bool> conditionStack = new Stack<bool>();
            conditionStack.Push(true);

            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                string line = lines[lineIdx].Trim();

                // Check for conditional directives
                if (line.StartsWith("%ifdef", StringComparison.OrdinalIgnoreCase))
                {
                    string sym = line.Substring(6).Trim();
                    bool parentActive = conditionStack.Peek();
                    bool active = parentActive && definedSymbols.Contains(sym);
                    conditionStack.Push(active);
                    output.AppendLine(); // preserve line numbers
                    continue;
                }
                else if (line.StartsWith("%ifndef", StringComparison.OrdinalIgnoreCase))
                {
                    string sym = line.Substring(7).Trim();
                    bool parentActive = conditionStack.Peek();
                    bool active = parentActive && !definedSymbols.Contains(sym);
                    conditionStack.Push(active);
                    output.AppendLine();
                    continue;
                }
                else if (line.StartsWith("%else", StringComparison.OrdinalIgnoreCase))
                {
                    if (conditionStack.Count > 1)
                    {
                        bool current = conditionStack.Pop();
                        bool parent = conditionStack.Peek();
                        conditionStack.Push(parent && !current);
                    }
                    output.AppendLine();
                    continue;
                }
                else if (line.StartsWith("%endif", StringComparison.OrdinalIgnoreCase))
                {
                    if (conditionStack.Count > 1)
                    {
                        conditionStack.Pop();
                    }
                    output.AppendLine();
                    continue;
                }

                // If currently inside an inactive block, ignore line
                if (!conditionStack.Peek())
                {
                    output.AppendLine();
                    continue;
                }

                // Handle %define NAME VALUE
                if (line.StartsWith("%define", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = line.Substring(7).Trim();
                    int spaceIdx = rest.IndexOf(' ');
                    if (spaceIdx > 0)
                    {
                        string sym = rest.Substring(0, spaceIdx).Trim();
                        string val = rest.Substring(spaceIdx + 1).Trim();
                        Define(sym, val);
                    }
                    else if (rest.Length > 0)
                    {
                        Define(rest);
                    }
                    output.AppendLine();
                    continue;
                }

                // Handle NAME equ VALUE
                int equIdx = line.IndexOf(" equ ", StringComparison.OrdinalIgnoreCase);
                if (equIdx > 0 && !line.StartsWith("//") && !line.StartsWith(";"))
                {
                    string sym = line.Substring(0, equIdx).Trim();
                    string val = line.Substring(equIdx + 5).Trim();
                    Define(sym, val);
                    output.AppendLine();
                    continue;
                }

                // Handle %include "file" or include "file"
                if (line.StartsWith("%include", StringComparison.OrdinalIgnoreCase) ||
                    (line.StartsWith("include", StringComparison.OrdinalIgnoreCase) && line.IndexOf('"') > 0))
                {
                    int quote1 = line.IndexOf('"');
                    int quote2 = line.LastIndexOf('"');
                    if (quote1 >= 0 && quote2 > quote1)
                    {
                        string incName = line.Substring(quote1 + 1, quote2 - quote1 - 1);
                        string searchBase = currentFile != null ? Path.GetDirectoryName(currentFile) : rootPath;
                        string incPath = Path.Combine(searchBase, incName);
                        if (!File.Exists(incPath))
                        {
                            incPath = Path.Combine(rootPath, incName);
                        }

                        if (File.Exists(incPath))
                        {
                            string fullInc = Path.GetFullPath(incPath);
                            if (!includedFiles.Contains(fullInc))
                            {
                                includedFiles.Add(fullInc);
                                string incContent = File.ReadAllText(fullInc);
                                string processedInc = Process(incContent, fullInc);
                                output.AppendLine(processedInc);
                            }
                            else
                            {
                                output.AppendLine(); // already included
                            }
                        }
                        else
                        {
                            throw new Exception(string.Format("Include file '{0}' not found (searched at '{1}').", incName, incPath));
                        }
                        continue;
                    }
                }

                // Apply define text replacements
                string processedLine = lines[lineIdx];
                foreach (var pair in defines)
                {
                    if (processedLine.Contains(pair.Key))
                    {
                        processedLine = processedLine.Replace(pair.Key, pair.Value);
                    }
                }

                output.AppendLine(processedLine);
            }

            return output.ToString();
        }
    }
}
