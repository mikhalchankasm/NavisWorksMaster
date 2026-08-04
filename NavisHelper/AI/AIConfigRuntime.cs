using System;
using System.Threading.Tasks;

namespace NavisHelper.AI
{
    internal sealed class AIConfigSnapshot
    {
        internal AIConfigSnapshot(
            string modelName,
            double temperature,
            int colorScheme)
        {
            ModelName = modelName ?? string.Empty;
            Temperature = temperature;
            ColorScheme = colorScheme;
        }

        internal string ModelName { get; }
        internal double Temperature { get; }
        internal int ColorScheme { get; }

        internal AIConfigData ToData()
        {
            return new AIConfigData
            {
                ModelName = ModelName,
                Temperature = Temperature,
                ColorScheme = ColorScheme
            };
        }
    }

    internal interface IAIConfigSnapshotPersistence
    {
        void Save(AIConfigSnapshot snapshot);
    }

    internal sealed class AIConfigRuntime
    {
        private readonly object _stateLock = new object();
        private readonly object _persistenceLock = new object();
        private readonly IAIConfigSnapshotPersistence _persistence;
        private string _modelName;
        private double _temperature;
        private int _colorScheme;
        private Task _persistenceTail = Task.CompletedTask;
        private long _latestPersistenceRequest;

        internal AIConfigRuntime(
            AIConfigSnapshot initialState,
            IAIConfigSnapshotPersistence persistence)
        {
            if (initialState == null)
                throw new ArgumentNullException(nameof(initialState));
            _persistence = persistence ??
                           throw new ArgumentNullException(nameof(persistence));
            _modelName = initialState.ModelName;
            _temperature = NormalizeTemperature(initialState.Temperature);
            _colorScheme = NormalizeColorScheme(initialState.ColorScheme);
        }

        internal AIConfigSnapshot Capture()
        {
            lock (_stateLock)
            {
                return new AIConfigSnapshot(
                    _modelName,
                    _temperature,
                    _colorScheme);
            }
        }

        internal void UpdateModelName(string modelName)
        {
            lock (_stateLock)
                _modelName = modelName ?? string.Empty;
        }

        internal int UpdateColorScheme(int colorScheme)
        {
            lock (_stateLock)
            {
                _colorScheme = NormalizeColorScheme(colorScheme);
                return _colorScheme;
            }
        }

        internal void Reset()
        {
            lock (_stateLock)
            {
                _modelName = string.Empty;
                _temperature = 0.3;
                _colorScheme = 8;
            }
        }

        internal Task PersistLatestAsync()
        {
            lock (_persistenceLock)
            {
                var request = ++_latestPersistenceRequest;
                var predecessor = _persistenceTail;
                _persistenceTail = PersistAfterAsync(predecessor, request);
                return _persistenceTail;
            }
        }

        private async Task PersistAfterAsync(Task predecessor, long request)
        {
            try
            {
                await predecessor.ConfigureAwait(false);
            }
            catch
            {
                // A failed write must not poison later persistence requests.
            }

            lock (_persistenceLock)
            {
                if (request != _latestPersistenceRequest)
                    return;
            }

            var latestState = Capture();
            await Task.Run(() => _persistence.Save(latestState))
                .ConfigureAwait(false);
        }

        private static double NormalizeTemperature(double temperature)
        {
            return temperature >= 0 && temperature <= 2
                ? temperature
                : 0.3;
        }

        private static int NormalizeColorScheme(int colorScheme)
        {
            return Math.Max(1, Math.Min(14, colorScheme));
        }
    }
}
