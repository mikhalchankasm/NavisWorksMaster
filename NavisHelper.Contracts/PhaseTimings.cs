using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Accumulates per-phase Stopwatch.GetTimestamp tick deltas for one tool call
    /// and formats them as one "phase_ms=..." log line, so the next speed-up can
    /// be chosen from a measurement instead of a static audit. Raw ticks are
    /// stored (one long per phase) and converted to milliseconds only on read,
    /// which keeps an Add per measured region far cheaper than the work it
    /// measures.
    /// </summary>
    public sealed class PhaseTimings
    {
        private readonly Dictionary<string, long> _ticksByPhase =
            new Dictionary<string, long>();
        private readonly List<string> _phasesInAddedOrder = new List<string>();

        public void Add(string phase, long elapsedTicks)
        {
            if (string.IsNullOrEmpty(phase))
                return;

            long total;
            if (_ticksByPhase.TryGetValue(phase, out total))
            {
                _ticksByPhase[phase] = total + elapsedTicks;
                return;
            }

            _ticksByPhase[phase] = elapsedTicks;
            _phasesInAddedOrder.Add(phase);
        }

        public long GetMilliseconds(string phase)
        {
            long total;
            return _ticksByPhase.TryGetValue(phase, out total)
                ? ToMilliseconds(total)
                : 0;
        }

        public string Format()
        {
            var builder = new StringBuilder();
            foreach (var phase in _phasesInAddedOrder)
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(phase)
                    .Append("_ms=")
                    .Append(ToMilliseconds(_ticksByPhase[phase]));
            }

            return builder.ToString();
        }

        private static long ToMilliseconds(long ticks)
        {
            return ticks * 1000 / Stopwatch.Frequency;
        }
    }
}
