using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    // SDK-independent runtime state, source-linked into the test project.
    internal sealed class ProtectedModeApprovals
    {
        private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);
        private readonly object _sync = new object();
        private readonly Func<DateTime> _utcNow;
        private readonly Func<int> _nextCode;
        private readonly Dictionary<Tuple<string, string>, GrantedApproval> _approved = new Dictionary<Tuple<string, string>, GrantedApproval>();
        private readonly List<GrantedApproval> _grantOrder = new List<GrantedApproval>();
        private readonly List<PendingApproval> _pending = new List<PendingApproval>();
        private bool _enabled;
        private long _documentEpoch;
        private long _revision;

        public event EventHandler Changed;

        public ProtectedModeApprovals(Func<DateTime> utcNow, Func<int> nextCode)
        {
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _nextCode = nextCode ?? throw new ArgumentNullException(nameof(nextCode));
        }

        public bool Enabled
        {
            get { lock (_sync) return _enabled; }
            set
            {
                WithNotification(() =>
                {
                    var changed = _enabled != value;
                    _enabled = value;
                    if (!value)
                    {
                        changed |= _approved.Count != 0 || _pending.Count != 0;
                        _approved.Clear();
                        _grantOrder.Clear();
                        _pending.Clear();
                    }
                    if (changed) _revision++;
                });
            }
        }

        public ApprovalDecision Check(string command, string documentKey, string sessionId, string clientName)
        {
            return WithNotification(() =>
            {
                var requiresApproval = ProtectedModeGate.RequiresApproval(command);
                if (!_enabled || !requiresApproval)
                    return new ApprovalDecision(true, null, new ApprovalTicket(command, documentKey,
                        sessionId, _documentEpoch, requiresApproval, null));

                if (string.IsNullOrWhiteSpace(sessionId))
                    return new ApprovalDecision(false, null, errorCode: ErrorCodes.SessionRequired);

                var now = _utcNow();
                ExpirePending(now);
                GrantedApproval grant;
                if (_approved.TryGetValue(Tuple.Create(documentKey, sessionId), out grant))
                    return new ApprovalDecision(true, null, new ApprovalTicket(command, documentKey,
                        sessionId, _documentEpoch, requiresApproval, grant.GrantId));

                var pending = _pending.FirstOrDefault(request =>
                    request.DocumentKey == documentKey && request.SessionId == sessionId);
                if (pending == null)
                {
                    pending = new PendingApproval(documentKey, sessionId, clientName, CreateCode(), now);
                    _pending.Add(pending);
                    _revision++;
                }
                return new ApprovalDecision(false, pending.Code);
            });
        }

        public bool Confirm(ApprovalTicket ticket)
        {
            return Confirm(ticket, null);
        }

        /// <summary>
        /// Final check on the UI thread right before the command runs. With protection off
        /// nothing is refused. With protection on, the ticket must carry a grant that still
        /// exists for the same document instance; when <paramref name="currentDocumentKey"/>
        /// is given, the active document must also be the one the request was accepted for.
        /// </summary>
        public bool Confirm(ApprovalTicket ticket, string currentDocumentKey)
        {
            lock (_sync)
            {
                if (ticket == null)
                    return false;
                if (!ticket.RequiresApproval || !_enabled)
                    return true;
                if (!ticket.GrantId.HasValue || ticket.DocumentEpoch != _documentEpoch)
                    return false;
                if (currentDocumentKey != null &&
                    !string.Equals(currentDocumentKey, ticket.DocumentKey, StringComparison.Ordinal))
                    return false;

                GrantedApproval grant;
                return _approved.TryGetValue(Tuple.Create(ticket.DocumentKey, ticket.SessionId), out grant)
                    && grant.GrantId == ticket.GrantId.Value;
            }
        }

        public IReadOnlyList<PendingApproval> Pending
        {
            get
            {
                return WithNotification<IReadOnlyList<PendingApproval>>(() =>
                {
                    ExpirePending(_utcNow());
                    // Immutable entries and a detached list prevent callers from mutating gate state.
                    return _pending.OrderBy(request => request.RequestedUtc).ToList().AsReadOnly();
                });
            }
        }

        public IReadOnlyList<GrantedApproval> Approved
        {
            get { lock (_sync) return _grantOrder.ToList().AsReadOnly(); }
        }

        public bool Approve(string code)
        {
            return WithNotification(() =>
            {
                var now = _utcNow();
                ExpirePending(now);
                var pending = _pending.FirstOrDefault(request => request.Code == code);
                if (pending == null)
                    return false;

                var key = Tuple.Create(pending.DocumentKey, pending.SessionId);
                GrantedApproval previous;
                if (_approved.TryGetValue(key, out previous)) _grantOrder.Remove(previous);
                var grant = new GrantedApproval(pending.DocumentKey, pending.SessionId,
                    pending.ClientName, Guid.NewGuid(), now);
                _approved[key] = grant;
                _grantOrder.Add(grant);
                _pending.Remove(pending);
                _revision++;
                return true;
            });
        }

        public bool Deny(string code)
        {
            return WithNotification(() =>
            {
                ExpirePending(_utcNow());
                var removed = _pending.RemoveAll(request => request.Code == code) != 0;
                if (removed) _revision++;
                return removed;
            });
        }

        public void Revoke(string documentKey)
        {
            WithNotification(() =>
            {
                foreach (var key in _approved.Keys.Where(key => key.Item1 == documentKey).ToList())
                    _approved.Remove(key);
                var removed = _grantOrder.RemoveAll(grant => grant.DocumentKey == documentKey);
                removed += _pending.RemoveAll(request => request.DocumentKey == documentKey);
                if (removed != 0) _revision++;
            });
        }

        public void Revoke(string documentKey, string sessionId)
        {
            WithNotification(() =>
            {
                var key = Tuple.Create(documentKey, sessionId);
                GrantedApproval grant;
                var removed = _approved.TryGetValue(key, out grant);
                if (removed)
                {
                    _approved.Remove(key);
                    _grantOrder.Remove(grant);
                }
                removed |= _pending.RemoveAll(request =>
                    request.DocumentKey == documentKey && request.SessionId == sessionId) != 0;
                if (removed) _revision++;
            });
        }

        public void DocumentChanged(string newDocumentKey)
        {
            WithNotification(() =>
            {
                _documentEpoch++;
                var changed = _approved.Count != 0;
                _approved.Clear();
                _grantOrder.Clear();
                changed |= _pending.RemoveAll(request => request.DocumentKey != newDocumentKey) != 0;
                if (changed) _revision++;
            });
        }

        // Called only while holding _sync; expiration is inclusive at ten minutes.
        private void ExpirePending(DateTime now)
        {
            if (_pending.RemoveAll(request => now - request.RequestedUtc >= RequestLifetime) != 0)
                _revision++;
        }

        private void WithNotification(Action action)
        {
            WithNotification(() => { action(); return true; });
        }

        private T WithNotification<T>(Func<T> action)
        {
            var changed = false;
            try
            {
                lock (_sync)
                {
                    var revision = _revision;
                    try { return action(); }
                    finally { changed = _revision != revision; }
                }
            }
            finally
            {
                // Notify after releasing the lock, including expiration before a failed operation.
                if (changed) Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private string CreateCode()
        {
            if (_pending.Count >= 10000)
                throw new InvalidOperationException("All approval codes are pending.");

            var used = new HashSet<string>(_pending.Select(request => request.Code), StringComparer.Ordinal);
            // Bound retries so a faulty generator cannot keep the gate lock forever.
            for (var attempt = 0; attempt < 10000; attempt++)
            {
                var number = ((_nextCode() % 10000) + 10000) % 10000;
                var code = number.ToString("D4", CultureInfo.InvariantCulture);
                if (!used.Contains(code))
                    return code;
            }
            throw new InvalidOperationException("The approval code generator did not produce an unused code.");
        }
    }

    internal sealed class ApprovalDecision
    {
        public ApprovalDecision(bool allowed, string code, ApprovalTicket ticket = null, string errorCode = ErrorCodes.ApprovalRequired)
        {
            Allowed = allowed;
            Code = code;
            ErrorCode = allowed ? null : errorCode;
            Ticket = ticket;
        }

        public bool Allowed { get; }
        public string Code { get; }
        public string ErrorCode { get; }
        public ApprovalTicket Ticket { get; }
    }

    internal sealed class ApprovalTicket
    {
        public ApprovalTicket(string command, string documentKey, string sessionId, long documentEpoch,
            bool requiresApproval, Guid? grantId)
        {
            Command = command;
            DocumentKey = documentKey;
            SessionId = sessionId;
            DocumentEpoch = documentEpoch;
            RequiresApproval = requiresApproval;
            GrantId = grantId;
        }

        public string Command { get; }
        public string DocumentKey { get; }
        public string SessionId { get; }
        public long DocumentEpoch { get; }
        public bool RequiresApproval { get; }
        public Guid? GrantId { get; }
    }

    internal sealed class GrantedApproval
    {
        public GrantedApproval(string documentKey, string sessionId, string clientName, Guid grantId, DateTime grantedUtc)
        {
            DocumentKey = documentKey;
            SessionId = sessionId;
            ClientName = clientName;
            GrantId = grantId;
            GrantedUtc = grantedUtc;
        }

        public string DocumentKey { get; }
        public string SessionId { get; }
        public string ClientName { get; }
        public Guid GrantId { get; }
        public DateTime GrantedUtc { get; }
    }

    internal sealed class PendingApproval
    {
        public PendingApproval(string documentKey, string sessionId, string clientName, string code, DateTime requestedUtc)
        {
            DocumentKey = documentKey;
            SessionId = sessionId;
            ClientName = clientName;
            Code = code;
            RequestedUtc = requestedUtc;
        }

        public string DocumentKey { get; }
        public string SessionId { get; }
        public string ClientName { get; }
        public string Code { get; }
        public DateTime RequestedUtc { get; }
    }
}
