using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NavisHelper.AI
{
    internal static class Program
    {
        private static async Task<int> Main()
        {
            AiWorkerRequestEnvelope request = null;
            AiWorkerResponseEnvelope response;
            try
            {
                var input = await Console.In.ReadToEndAsync()
                    .ConfigureAwait(false);
                request = JsonConvert.DeserializeObject<
                    AiWorkerRequestEnvelope>(input);
                var key = Environment.GetEnvironmentVariable(
                    AiWorkerProtocol.KeyEnvironmentVariable);
                using (var client = new OpenRouterClient())
                {
                    response = await new AiWorkerRequestHandler(client)
                        .HandleAsync(
                            request,
                            key,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (JsonException)
            {
                response = AiWorkerResponseEnvelope.Failure(
                    request,
                    OpenRouterFailureKind.ProtocolMismatch);
            }
            catch (Exception)
            {
                Console.Error.WriteLine("worker_internal_error");
                response = AiWorkerResponseEnvelope.Failure(
                    request,
                    OpenRouterFailureKind.WorkerInternalFailure);
            }

            Console.Out.WriteLine(JsonConvert.SerializeObject(
                response,
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }));
            return 0;
        }
    }
}
