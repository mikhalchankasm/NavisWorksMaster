namespace NavisHelper.McpServer.Tools;

[Flags]
internal enum ToolEffects
{
    None = 0,
    View = 1,
    Document = 2,
    Files = 4,
    Host = 8,
    LocalState = 16,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ToolCapabilitiesAttribute : Attribute
{
    public ToolCapabilitiesAttribute(ToolEffects effects)
    {
        Effects = effects;
    }

    public ToolEffects Effects { get; }

    public bool RequiresHost { get; set; }

    public bool RequiresDocument { get; set; }
}
