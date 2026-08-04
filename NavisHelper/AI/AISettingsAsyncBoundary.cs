using System;
using System.Threading.Tasks;

namespace NavisHelper.AI
{
    internal interface IAISettingsUiBoundary
    {
        Task RunAsync(Action action);
    }

    internal static class AISettingsAsyncBoundary
    {
        internal static async Task RunAsync(
            Func<Task> operation,
            Func<Exception, Task> handleUnexpectedFailure)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Lifecycle and stage cancellation are normal completion paths.
            }
            catch (Exception ex)
            {
                try
                {
                    if (handleUnexpectedFailure != null)
                    {
                        await handleUnexpectedFailure(ex)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    // An event boundary must never fault while reporting a fault.
                }
            }
        }
    }

    internal sealed class AISettingsUiMutationGate
    {
        private readonly IAISettingsUiBoundary _boundary;

        internal AISettingsUiMutationGate(IAISettingsUiBoundary boundary)
        {
            _boundary = boundary ??
                        throw new ArgumentNullException(nameof(boundary));
        }

        internal Task RunAsync(Func<bool> mayApply, Action mutation)
        {
            if (mayApply == null)
                throw new ArgumentNullException(nameof(mayApply));
            if (mutation == null)
                throw new ArgumentNullException(nameof(mutation));

            return _boundary.RunAsync(() =>
            {
                if (mayApply())
                    mutation();
            });
        }
    }
}
