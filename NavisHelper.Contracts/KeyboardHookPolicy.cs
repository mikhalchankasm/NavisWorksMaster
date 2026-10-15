namespace NavisHelper.Agent.Contracts
{
    public static class KeyboardHookPolicy
    {
        public static bool ShouldDispatch(int nCode, long lParam)
        {
            // WH_KEYBOARD: bit 31 marks key up; bit 29 marks Alt context.
            const long suppressedFlags = (1L << 31) | (1L << 29);
            return nCode == 0 && (lParam & suppressedFlags) == 0;
        }
    }
}
