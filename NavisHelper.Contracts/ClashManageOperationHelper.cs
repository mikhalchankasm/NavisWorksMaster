namespace NavisHelper.Agent.Contracts
{
    public static class ClashManageOperationHelper
    {
        public const string Run = "run";
        public const string Reset = "reset";
        public const string Compact = "compact";
        public const string Rename = "rename";
        public const string RenameBatch = "rename_batch";
        public const string Delete = "delete";
        public const string Move = "move";
        public const string Sort = "sort";
        public const string SetSettings = "set_settings";

        public static string NormalizeOperation(string operation)
        {
            var value = (operation ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case Run:
                case "execute":
                case "выполнить":
                case "запуск":
                    return Run;
                case Reset:
                case "clear":
                case "сброс":
                case "очистить":
                    return Reset;
                case Compact:
                case "compress":
                case "сжать":
                    return Compact;
                case Rename:
                case "переименовать":
                    return Rename;
                case RenameBatch:
                case "batch_rename":
                case "переименовать_пакет":
                    return RenameBatch;
                case Delete:
                case "remove":
                case "удалить":
                    return Delete;
                case Move:
                case "reorder":
                case "переместить":
                case "порядок":
                    return Move;
                case Sort:
                case "sort_by_name":
                case "sort_name":
                case "сортировать":
                    return Sort;
                case SetSettings:
                case "settings":
                case "configure":
                case "config":
                case "настройки":
                case "настроить":
                    return SetSettings;
                default:
                    return null;
            }
        }
    }
}
