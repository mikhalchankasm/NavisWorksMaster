using System.Threading;

namespace NavisHelper.Agent.Contracts
{
    public enum UiCallbackAdmissionState
    {
        Queued,
        Running,
        Abandoned
    }

    public sealed class UiCallbackAdmission
    {
        private int _state = (int)UiCallbackAdmissionState.Queued;

        public UiCallbackAdmissionState State
        {
            get { return (UiCallbackAdmissionState)Volatile.Read(ref _state); }
        }

        public bool TryStart()
        {
            return Interlocked.CompareExchange(ref _state, (int)UiCallbackAdmissionState.Running,
                (int)UiCallbackAdmissionState.Queued) == (int)UiCallbackAdmissionState.Queued;
        }

        public bool TryAbandon()
        {
            return Interlocked.CompareExchange(ref _state, (int)UiCallbackAdmissionState.Abandoned,
                (int)UiCallbackAdmissionState.Queued) == (int)UiCallbackAdmissionState.Queued;
        }
    }
}
