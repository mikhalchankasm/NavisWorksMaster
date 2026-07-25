using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Agent.Host
{
    internal sealed class CommandRouter
    {
        private readonly Dictionary<string, CommandRoute> _routes =
            new Dictionary<string, CommandRoute>(StringComparer.OrdinalIgnoreCase);

        internal void Register<TRequest>(
            string commandName,
            bool requiresDocument,
            Func<JToken, TRequest> deserialize,
            Func<Document, TRequest, object> handler)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                throw new ArgumentException("Command name is required.", nameof(commandName));
            if (deserialize == null)
                throw new ArgumentNullException(nameof(deserialize));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_routes.ContainsKey(commandName))
                throw new InvalidOperationException("Duplicate command route: " + commandName);

            _routes.Add(
                commandName,
                new CommandRoute(
                    requiresDocument,
                    (document, payloadToken) => handler(document, deserialize(payloadToken))));
        }

        internal bool TryDispatch(
            string commandName,
            Document document,
            JToken payloadToken,
            Action<Document> ensureDocument,
            out object response)
        {
            CommandRoute route;
            if (!_routes.TryGetValue(commandName, out route))
            {
                response = null;
                return false;
            }

            if (route.RequiresDocument)
                ensureDocument(document);

            response = route.Handler(document, payloadToken);
            return true;
        }

        private sealed class CommandRoute
        {
            internal CommandRoute(bool requiresDocument, Func<Document, JToken, object> handler)
            {
                RequiresDocument = requiresDocument;
                Handler = handler;
            }

            internal bool RequiresDocument { get; }

            internal Func<Document, JToken, object> Handler { get; }
        }
    }
}
