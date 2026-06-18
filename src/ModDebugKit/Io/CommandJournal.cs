using System;
using System.IO;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;

namespace ModDebugKit.Io
{
    /// <summary>
    /// Appends executed-command results to <c>out.jsonl</c>. Shared by the file
    /// channel and the script runner so every command's result is journaled the
    /// same way, whoever issued it.
    /// </summary>
    public static class CommandJournal
    {
        private static readonly object Sync = new();

        public static void Append(DbgResult result)
        {
            try
            {
                var json = DbgJson.Line(result);
                lock (Sync)
                {
                    File.AppendAllText(ModDebugKitRuntime.Paths.CommandOut, json + Environment.NewLine);
                }
            }
            catch (Exception e)
            {
                DbgLog.Error("Failed to append a command result to out.jsonl.", e);
            }
        }
    }
}
