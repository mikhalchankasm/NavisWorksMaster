using System;
using System.Threading;

namespace NavisHelper.AI
{
    internal interface IEnvironmentVariableAccessor
    {
        string Get(string name, EnvironmentVariableTarget target);
        void Set(string name, string value, EnvironmentVariableTarget target);
    }

    internal sealed class SystemEnvironmentVariableAccessor :
        IEnvironmentVariableAccessor
    {
        public string Get(string name, EnvironmentVariableTarget target)
        {
            return Environment.GetEnvironmentVariable(name, target);
        }

        public void Set(
            string name,
            string value,
            EnvironmentVariableTarget target)
        {
            Environment.SetEnvironmentVariable(name, value, target);
        }
    }

    internal sealed class KeyStoreMutationResult
    {
        internal KeyStoreMutationResult(
            bool isSuccess,
            bool hasUserValue,
            bool hasProcessValue,
            bool hasRuntimeValue,
            bool generationMatched,
            int generation)
        {
            IsSuccess = isSuccess;
            HasUserValue = hasUserValue;
            HasProcessValue = hasProcessValue;
            HasRuntimeValue = hasRuntimeValue;
            GenerationMatched = generationMatched;
            Generation = generation;
        }

        internal bool IsSuccess { get; }
        internal bool HasUserValue { get; }
        internal bool HasProcessValue { get; }
        internal bool HasRuntimeValue { get; }
        internal bool GenerationMatched { get; }
        internal int Generation { get; }
        internal bool HasAnyValue =>
            HasUserValue || HasProcessValue || HasRuntimeValue;
        internal bool IsFullyConnected =>
            HasUserValue && HasProcessValue && HasRuntimeValue;
        internal bool IsFullyDisconnected => !HasAnyValue;
    }

    internal sealed class OpenRouterKeySnapshot
    {
        internal OpenRouterKeySnapshot(string key, int generation)
        {
            Key = key;
            Generation = generation;
        }

        internal string Key { get; }
        internal int Generation { get; }
    }

    internal sealed class OpenRouterKeyStore
    {
        internal const string EnvironmentVariableName = "OPEN_ROUTER_NW_KEY";

        private static readonly Lazy<OpenRouterKeyStore> LazyCurrent =
            new Lazy<OpenRouterKeyStore>(
                () => new OpenRouterKeyStore(
                    new SystemEnvironmentVariableAccessor()));

        private readonly object _sync = new object();
        private readonly IEnvironmentVariableAccessor _environment;
        private string _runtimeKey;
        private int _generation;

        internal OpenRouterKeyStore(IEnvironmentVariableAccessor environment)
        {
            _environment = environment ??
                           throw new ArgumentNullException(nameof(environment));
        }

        internal static OpenRouterKeyStore Current => LazyCurrent.Value;

        internal int Generation
        {
            get
            {
                lock (_sync)
                    return _generation;
            }
        }

        internal bool HasKey => !string.IsNullOrWhiteSpace(GetKey());

        internal string GetKey()
        {
            lock (_sync)
                return GetKeyUnsafe();
        }

        internal OpenRouterKeySnapshot Capture()
        {
            lock (_sync)
                return new OpenRouterKeySnapshot(GetKeyUnsafe(), _generation);
        }

        internal KeyStoreMutationResult ActivateExistingKey(string validatedKey)
        {
            return ActivateExistingKey(validatedKey, null);
        }

        internal KeyStoreMutationResult TryActivateExistingKey(
            string validatedKey,
            int expectedGeneration)
        {
            return ActivateExistingKey(
                validatedKey,
                expectedGeneration,
                CancellationToken.None);
        }

        internal KeyStoreMutationResult TryActivateExistingKey(
            string validatedKey,
            int expectedGeneration,
            CancellationToken cancellationToken)
        {
            return ActivateExistingKey(
                validatedKey,
                expectedGeneration,
                cancellationToken);
        }

        private KeyStoreMutationResult ActivateExistingKey(
            string validatedKey,
            int? expectedGeneration,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(validatedKey))
                return CurrentState(false);

            lock (_sync)
            {
                if (expectedGeneration.HasValue &&
                    expectedGeneration.Value != _generation)
                    return CurrentStateUnsafe(false, false);
                var previousProcess = Read(EnvironmentVariableTarget.Process);
                var previousRuntime = _runtimeKey;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _environment.Set(
                        EnvironmentVariableName,
                        validatedKey,
                        EnvironmentVariableTarget.Process);
                    cancellationToken.ThrowIfCancellationRequested();
                    _runtimeKey = validatedKey;
                    _generation++;
                    return CurrentStateUnsafe(true);
                }
                catch
                {
                    TrySet(previousProcess, EnvironmentVariableTarget.Process);
                    _runtimeKey = previousRuntime;
                    return CurrentStateUnsafe(false);
                }
            }
        }

        internal KeyStoreMutationResult SaveValidatedKey(string validatedKey)
        {
            return SaveValidatedKey(validatedKey, null);
        }

        internal KeyStoreMutationResult TrySaveValidatedKey(
            string validatedKey,
            int expectedGeneration)
        {
            return SaveValidatedKey(
                validatedKey,
                expectedGeneration,
                CancellationToken.None);
        }

        internal KeyStoreMutationResult TrySaveValidatedKey(
            string validatedKey,
            int expectedGeneration,
            CancellationToken cancellationToken)
        {
            return SaveValidatedKey(
                validatedKey,
                expectedGeneration,
                cancellationToken);
        }

        private KeyStoreMutationResult SaveValidatedKey(
            string validatedKey,
            int? expectedGeneration,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(validatedKey))
                return CurrentState(false);

            lock (_sync)
            {
                if (expectedGeneration.HasValue &&
                    expectedGeneration.Value != _generation)
                    return CurrentStateUnsafe(false, false);
                var previousUser = Read(EnvironmentVariableTarget.User);
                var previousProcess = Read(EnvironmentVariableTarget.Process);
                var previousRuntime = _runtimeKey;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _environment.Set(
                        EnvironmentVariableName,
                        validatedKey,
                        EnvironmentVariableTarget.User);
                    cancellationToken.ThrowIfCancellationRequested();
                    _environment.Set(
                        EnvironmentVariableName,
                        validatedKey,
                        EnvironmentVariableTarget.Process);
                    cancellationToken.ThrowIfCancellationRequested();
                    _runtimeKey = validatedKey;
                    _generation++;
                    return CurrentStateUnsafe(true);
                }
                catch
                {
                    TrySet(previousUser, EnvironmentVariableTarget.User);
                    TrySet(previousProcess, EnvironmentVariableTarget.Process);
                    _runtimeKey = previousRuntime;
                    return CurrentStateUnsafe(false);
                }
            }
        }

        internal KeyStoreMutationResult Disconnect()
        {
            lock (_sync)
            {
                var userDeleted = TrySet(
                    null,
                    EnvironmentVariableTarget.User);
                var processDeleted = TrySet(
                    null,
                    EnvironmentVariableTarget.Process);
                _runtimeKey = null;
                _generation++;
                var state = CurrentStateUnsafe(
                    userDeleted && processDeleted);
                return new KeyStoreMutationResult(
                    state.IsSuccess && state.IsFullyDisconnected,
                    state.HasUserValue,
                    state.HasProcessValue,
                    state.HasRuntimeValue,
                    true,
                    _generation);
            }
        }

        private KeyStoreMutationResult CurrentState(bool success)
        {
            lock (_sync)
                return CurrentStateUnsafe(success);
        }

        private KeyStoreMutationResult CurrentStateUnsafe(
            bool success,
            bool generationMatched = true)
        {
            return new KeyStoreMutationResult(
                success,
                !string.IsNullOrWhiteSpace(Read(EnvironmentVariableTarget.User)),
                !string.IsNullOrWhiteSpace(Read(EnvironmentVariableTarget.Process)),
                !string.IsNullOrWhiteSpace(_runtimeKey),
                generationMatched,
                _generation);
        }

        private string Read(EnvironmentVariableTarget target)
        {
            try
            {
                return _environment.Get(EnvironmentVariableName, target);
            }
            catch
            {
                return null;
            }
        }

        private string GetKeyUnsafe()
        {
            if (!string.IsNullOrWhiteSpace(_runtimeKey))
                return _runtimeKey;

            var processKey = Read(EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(processKey))
                return processKey;

            return Read(EnvironmentVariableTarget.User);
        }

        private bool TrySet(string value, EnvironmentVariableTarget target)
        {
            try
            {
                _environment.Set(EnvironmentVariableName, value, target);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
