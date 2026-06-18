using System;
using System.IO;
using System.Text;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;

namespace ModDebugKit.Io
{
    /// <summary>
    /// The spine of the kit: watches <c>io/in.txt</c> for <em>appended</em>
    /// lines, dispatches each on the main thread, and appends the structured
    /// result to <c>io/out.jsonl</c>. An agent drives the game by appending a
    /// line and reading the result file — no console keystrokes, no
    /// screenshots.
    ///
    /// Reads with a byte cursor that only advances past complete lines, so a
    /// partially-written append is left for the next poll (UTF-8 safe); a file
    /// shorter than the cursor is treated as truncated/recreated and re-read
    /// from the top. Backlog present at load is skipped — only lines appended
    /// after the module is up are executed, so a stale file can't auto-run last
    /// session's commands.
    /// </summary>
    public sealed class FileCommandChannel
    {
        private const float PollIntervalSeconds = 0.2f;

        private readonly DbgPaths _paths;
        private readonly CommandDispatcher _dispatcher;

        private long _offset;
        private float _accum;
        private bool _started;

        public FileCommandChannel(DbgPaths paths, CommandDispatcher dispatcher)
        {
            _paths = paths;
            _dispatcher = dispatcher;
        }

        public void Start()
        {
            try
            {
                Directory.CreateDirectory(_paths.IoDir);
                // Truncate to a fresh command file each session: no stale backlog from a prior
                // session re-runs, and no ambiguity about commands appended during module load
                // (an offset-skip silently dropped those). The file then only ever holds
                // this-session commands, processed from byte 0.
                File.WriteAllText(_paths.CommandIn, string.Empty);
                _offset = 0;
                _started = true;
                DbgLog.Info($"File channel: watching '{_paths.CommandIn}' (fresh); results -> '{_paths.CommandOut}'.");
            }
            catch (Exception e)
            {
                DbgLog.Error("File channel failed to start; file-driven commands are disabled this session.", e);
            }
        }

        /// <summary>Called every application frame; throttled internally.</summary>
        public void Tick(float dt)
        {
            if (!_started)
                return;

            _accum += dt;
            if (_accum < PollIntervalSeconds)
                return;
            _accum = 0f;

            try
            {
                Pump();
            }
            catch (Exception e)
            {
                DbgLog.Error("File channel pump failed (will retry next poll).", e);
            }
        }

        private void Pump()
        {
            if (!File.Exists(_paths.CommandIn))
                return;

            string chunk;
            using (var fs = new FileStream(_paths.CommandIn, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var len = fs.Length;
                if (len < _offset)
                    _offset = 0; // truncated or recreated since last poll
                if (len == _offset)
                    return;

                fs.Seek(_offset, SeekOrigin.Begin);
                var pending = (int)(len - _offset);
                var buffer = new byte[pending];
                var read = 0;
                while (read < pending)
                {
                    var n = fs.Read(buffer, read, pending - read);
                    if (n == 0)
                        break;
                    read += n;
                }

                var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
                if (lastNewline < 0)
                    return; // no complete line yet; leave the cursor and wait

                chunk = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
                _offset += lastNewline + 1;
            }

            foreach (var line in chunk.Split('\n'))
                ProcessLine(line.TrimEnd('\r'));
        }

        private void ProcessLine(string line)
        {
            if (!DbgCommandParser.TryParse(line, out var command, out _))
                return;

            var result = _dispatcher.Execute(command);
            CommandJournal.Append(result);
        }
    }
}
