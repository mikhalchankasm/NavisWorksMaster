using System;

namespace NavisHelper.AI
{
    internal sealed class OpenRouterCatalogCache
    {
        private static readonly Lazy<OpenRouterCatalogCache> LazyCurrent =
            new Lazy<OpenRouterCatalogCache>(
                () => new OpenRouterCatalogCache(TimeSpan.FromMinutes(10)));

        private readonly object _sync = new object();
        private readonly TimeSpan _lifetime;
        private OpenRouterCatalogResult _catalog;
        private DateTime _storedAtUtc;
        private int _keyGeneration = -1;

        internal OpenRouterCatalogCache(TimeSpan lifetime)
        {
            if (lifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            _lifetime = lifetime;
        }

        internal static OpenRouterCatalogCache Current => LazyCurrent.Value;

        internal OpenRouterCatalogResult TryGet(
            int keyGeneration,
            DateTime utcNow)
        {
            lock (_sync)
            {
                if (_catalog == null ||
                    !_catalog.IsAvailable ||
                    _keyGeneration != keyGeneration ||
                    utcNow - _storedAtUtc > _lifetime)
                    return null;
                return _catalog;
            }
        }

        internal void Store(
            int keyGeneration,
            OpenRouterCatalogResult catalog,
            DateTime utcNow)
        {
            if (catalog == null || !catalog.IsAvailable)
                return;
            lock (_sync)
            {
                _catalog = catalog;
                _keyGeneration = keyGeneration;
                _storedAtUtc = utcNow;
            }
        }

        internal void Invalidate()
        {
            lock (_sync)
            {
                _catalog = null;
                _keyGeneration = -1;
                _storedAtUtc = default(DateTime);
            }
        }
    }

    internal sealed class OpenRouterModelPolicy
    {
        private OpenRouterModelPolicy(
            bool maySendChat,
            OpenRouterModelInfo model,
            AiColorOutcome failure)
        {
            MaySendChat = maySendChat;
            Model = model;
            Failure = failure;
        }

        internal bool MaySendChat { get; }
        internal OpenRouterModelInfo Model { get; }
        internal AiColorOutcome Failure { get; }

        internal static OpenRouterModelPolicy Evaluate(
            OpenRouterCatalogResult catalog,
            string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return Block(AiColorOutcomeKind.ModelNotSelected);
            if (catalog == null || !catalog.IsAvailable)
            {
                return new OpenRouterModelPolicy(
                    false,
                    null,
                    catalog == null
                        ? AiColorOutcome.Failure(
                            AiColorOutcomeKind.CatalogUnavailable)
                        : AiColorOutcome.Failure(
                            MapCatalogFailure(catalog.FailureKind),
                            catalog.HttpStatus));
            }

            OpenRouterModelInfo model;
            if (!catalog.Models.TryGetValue(modelId, out model))
                return Block(AiColorOutcomeKind.ModelUnavailable);
            if (!model.IsColoringCompatible)
                return Block(AiColorOutcomeKind.ModelIncompatible);

            return new OpenRouterModelPolicy(
                true,
                model,
                null);
        }

        private static OpenRouterModelPolicy Block(AiColorOutcomeKind kind)
        {
            return new OpenRouterModelPolicy(
                false,
                null,
                AiColorOutcome.Failure(kind));
        }

        private static AiColorOutcomeKind MapCatalogFailure(
            OpenRouterFailureKind failureKind)
        {
            switch (failureKind)
            {
                case OpenRouterFailureKind.Unauthorized:
                    return AiColorOutcomeKind.Unauthorized;
                case OpenRouterFailureKind.RateLimited:
                    return AiColorOutcomeKind.RateLimited;
                case OpenRouterFailureKind.Cancelled:
                    return AiColorOutcomeKind.Cancelled;
                default:
                    return AiColorOutcomeKind.CatalogUnavailable;
            }
        }
    }
}
