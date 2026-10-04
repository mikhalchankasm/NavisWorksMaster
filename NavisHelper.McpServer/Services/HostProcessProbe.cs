using System.Diagnostics;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Diagnostics;

// Shared source with the configurator: one authority for host process identity.
internal sealed record ProcessIdentityEvidence(string Status, DateTime? StartedAtUtc = null);

internal static class HostProcessProbe
{
    internal static ProcessIdentityEvidence Inspect(InstanceDiscoveryRecord record, bool requireStartTime)
    {
        if (record == null || record.Pid <= 0)
            return new("stale");
        try
        {
            using var process = Process.GetProcessById(record.Pid);
            return Classify(process.ProcessName, process.HasExited, record.ProcessStartedAtUtc,
                requireStartTime, () => process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException) { return new("stale"); }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return new("unverified");
        }
    }

    internal static ProcessIdentityEvidence Classify(string name, bool exited, DateTime? recorded,
        bool requireStartTime, Func<DateTime> readStart)
    {
        if (exited || (!string.Equals(name, "Roamer", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Navisworks", StringComparison.OrdinalIgnoreCase)))
            return new("stale");
        // Preserve the server's legacy handling without reading unavailable start-time data.
        if (!recorded.HasValue)
            return new(requireStartTime ? "unverified" : "alive");
        return CompareStart(recorded, readStart());
    }

    internal static ProcessIdentityEvidence CompareStart(DateTime? recorded, DateTime actual)
    {
        if (!recorded.HasValue)
            return new("unverified");
        return (actual - recorded.Value).Duration() > TimeSpan.FromSeconds(2)
            ? new("stale") : new("alive", actual);
    }
}
