namespace NavisHelper.Agent.Contracts
{
    public static class ClashTestTypeHelper
    {
        public const string Hard = "hard";
        public const string HardConservative = "hard_conservative";
        public const string Clearance = "clearance";
        public const string Duplicate = "duplicate";

        public static string NormalizeTestType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Trim().ToLowerInvariant()
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("/", string.Empty);
            normalized = normalized
                .Replace("(", string.Empty)
                .Replace(")", string.Empty);

            switch (normalized)
            {
                case "hard":
                case "intersection":
                case "hardintersection":
                case "intersect":
                case "пересечение":
                case "попересечению":
                    return Hard;
                case "hardconservative":
                case "conservative":
                case "conservativehardconservative":
                case "hardconservativeconservative":
                case "консервативно":
                case "пересечениеконсервативно":
                case "попересечениюконсервативно":
                    return HardConservative;
                case "clearance":
                case "clear":
                case "gap":
                case "просвет":
                    return Clearance;
                case "duplicate":
                case "duplicates":
                case "duplication":
                case "дублирование":
                case "дубликаты":
                    return Duplicate;
                default:
                    return null;
            }
        }
    }
}
