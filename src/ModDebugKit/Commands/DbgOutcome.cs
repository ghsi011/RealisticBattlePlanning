namespace ModDebugKit.Commands
{
    /// <summary>
    /// What a command handler returns: a success/failure flag, a one-line
    /// message, and an optional structured payload. The dispatcher wraps this
    /// into the serializable <see cref="DbgResult"/> (stamping seq/time and
    /// catching exceptions), so handlers stay small and engine-focused.
    /// </summary>
    public sealed class DbgOutcome
    {
        private DbgOutcome(bool ok, string message, object data)
        {
            Ok = ok;
            Message = message;
            Data = data;
        }

        public bool Ok { get; }
        public string Message { get; }
        public object Data { get; }

        public static DbgOutcome Success(string message, object data = null) => new(true, message, data);

        public static DbgOutcome Failure(string message, object data = null) => new(false, message, data);
    }
}
