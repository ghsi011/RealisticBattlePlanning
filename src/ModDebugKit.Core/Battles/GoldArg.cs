namespace ModDebugKit.Battles
{
    /// <summary>
    /// Parses the <c>dbg.camp.gold</c> argument into a delta: a bare number
    /// <c>n</c> SETS gold (delta = n - current), while a signed <c>+n</c>/<c>-n</c>
    /// ADJUSTS it (delta = n). Engine-free so the set-vs-adjust rule is unit-tested.
    /// </summary>
    public static class GoldArg
    {
        public static bool TryResolveDelta(string arg, int current, out int delta)
        {
            delta = 0;
            if (string.IsNullOrWhiteSpace(arg) || !int.TryParse(arg, out var value))
                return false;
            var relative = arg[0] == '+' || arg[0] == '-';
            delta = relative ? value : value - current;
            return true;
        }
    }
}
