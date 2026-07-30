using System;

namespace NavisHelper.Core
{
    internal static class ClashOperationProtocolReason
    {
        internal static string For(string operation)
        {
            switch (operation)
            {
                case "run":
                    return "Clash Test: выполнено";
                case "reset":
                    return "Clash Test: сброшено";
                case "compact":
                    return "Clash Test: сжато";
                case "delete":
                    return "Clash Test: удалено";
                default:
                    return "Clash Test: " + (operation ?? string.Empty);
            }
        }
    }
}
