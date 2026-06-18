using System;
using System.IO;
using ModDebugKit.Diagnostics;
using Path = System.IO.Path;

namespace ModDebugKit.Io
{
    /// <summary>
    /// Shared resolve-and-load for the preset/script libraries: a bare
    /// <c>name</c> maps to <c>&lt;libraryDir&gt;/&lt;name&gt;.json</c>; anything
    /// with a path separator or a <c>.json</c> suffix is resolved relative to
    /// the output root (or absolute). One implementation so <c>dbg.battle</c>
    /// and <c>dbg.run</c> can't drift.
    /// </summary>
    public static class JsonFileLibrary
    {
        public static bool TryLoad<T>(string label, string arg, string libraryDir, out T value, out string path, out string error)
            where T : class
        {
            value = null;
            error = null;

            var looksLikePath = arg.IndexOf('/') >= 0 || arg.IndexOf('\\') >= 0 ||
                                arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            path = looksLikePath ? ModDebugKitRuntime.Paths.Resolve(arg) : Path.Combine(libraryDir, arg + ".json");

            if (!File.Exists(path))
            {
                error = $"{label} not found: {path}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                error = $"could not read {label} '{path}': {e.Message}";
                return false;
            }

            if (!DbgJson.TryDeserialize(json, out value, out var parseError))
            {
                error = $"{label} parse error in '{path}': {parseError}";
                return false;
            }

            return true;
        }
    }
}
