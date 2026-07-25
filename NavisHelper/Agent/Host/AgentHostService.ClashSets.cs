using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService
    {
        private ClashTestsFromSetsResponse CreateClashTestsFromSets(Document document, ClashTestsFromSetsRequest request)
        {
            var response = _clashTestsFromSetsService.Execute(document, request);
            if (request != null && request.Apply == true && request.RunAfterCreate == true && response.CreatedTestCount > 0)
            {
                var run = _clashBatchRunService.StartNames(
                    response.Tests.FindAll(item => item != null && item.Applied).ConvertAll(item => item.TestName),
                    request.RunBatchSize.GetValueOrDefault(10),
                    request.PerTestTimeoutSeconds.GetValueOrDefault(300),
                    document);
                response.RunOperationId = run.OperationId;
            }
            return response;
        }
    }
}
