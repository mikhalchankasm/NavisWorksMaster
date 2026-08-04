using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NavisHelper.AI
{
    internal interface IOpenRouterTransport
    {
        Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken cancellationToken);

        Task<OpenRouterCatalogResult> GetModelsAsync(
            string key,
            CancellationToken cancellationToken);

        Task<AiColorOutcome> GetColorsAsync(
            string key,
            IReadOnlyCollection<string> objectNames,
            string schemeName,
            OpenRouterModelInfo model,
            double temperature,
            CancellationToken cancellationToken);
    }
}
