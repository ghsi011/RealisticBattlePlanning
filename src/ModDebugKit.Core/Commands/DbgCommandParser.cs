using System.Collections.Generic;
using System.Text;

namespace ModDebugKit.Commands
{
    /// <summary>
    /// Turns a raw input line into a <see cref="DbgCommand"/>. Whitespace
    /// separates tokens; double quotes group a token containing spaces
    /// (<c>dbg.shot "my shot"</c>); a backslash escapes the next character
    /// inside or outside quotes. Blank lines and lines beginning with
    /// <c>#</c> or <c>//</c> are comments and parse to nothing.
    /// </summary>
    public static class DbgCommandParser
    {
        // UTF-8 byte-order mark (U+FEFF). A tool appending to a fresh file
        // (e.g. PowerShell's Add-Content -Encoding UTF8) can prepend it; it
        // then lands on the first line and must be stripped before parsing.
        private const char Bom = '﻿';

        /// <summary>
        /// Parses <paramref name="raw"/>. Returns false for a blank/comment
        /// line (with <paramref name="skipReason"/> set), true otherwise.
        /// </summary>
        public static bool TryParse(string raw, out DbgCommand command, out string skipReason)
        {
            command = null;
            skipReason = null;

            var line = raw?.TrimStart(Bom).Trim();
            if (string.IsNullOrEmpty(line))
            {
                skipReason = "blank";
                return false;
            }
            if (line.StartsWith("#") || line.StartsWith("//"))
            {
                skipReason = "comment";
                return false;
            }

            var tokens = Tokenize(line);
            if (tokens.Count == 0)
            {
                skipReason = "no tokens";
                return false;
            }

            var head = tokens[0];
            var dot = head.IndexOf('.');
            string ns, name;
            if (dot < 0)
            {
                ns = string.Empty;
                name = head;
            }
            else
            {
                ns = head.Substring(0, dot);
                name = head.Substring(dot + 1);
            }

            var args = tokens.GetRange(1, tokens.Count - 1);
            command = new DbgCommand(ns, name, args, raw);
            return true;
        }

        private static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            var inToken = false;
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '\\' && i + 1 < line.Length)
                {
                    current.Append(line[++i]);
                    inToken = true;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    inToken = true;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (inToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        inToken = false;
                    }
                    continue;
                }

                current.Append(c);
                inToken = true;
            }

            if (inToken)
                tokens.Add(current.ToString());

            return tokens;
        }
    }
}
