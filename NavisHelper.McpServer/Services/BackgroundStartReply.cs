namespace NavisHelper.McpServer.Services;

/// <summary>
/// The words a background=true call answers with at once (#523). The reply reuses the tool's own
/// response type, whose counts stay at their defaults until the job ends, so it has to say so.
/// </summary>
public static class BackgroundStartReply
{
    public static string Message(string operationId) =>
        "Started in the background as " + operationId + "; the counts in this reply are not filled. " +
        "Call background_job_status with this operationId for the result.";
}
