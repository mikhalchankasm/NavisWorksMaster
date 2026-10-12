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
        private readonly HashSet<Tuple<string, string>> _approved = new HashSet<Tuple<string, string>>();
        private readonly List<PendingApproval> _pending = new List<PendingApproval>();
        private bool _enabled;

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
                lock (_sync)
                {
                    _enabled = value;
                    if (!value)
                    {
                        _approved.Clear();
                        _pending.Clear();
                    }
                }
            }
        }

        public ApprovalDecision Check(string command, string documentKey, string sessionId, string clientName)
        {
            lock (_sync)
            {
                if (!_enabled || !HostCommandEffects.RequiresApproval(command))
                    return new ApprovalDecision(true, null);

                var now = _utcNow();
                ExpirePending(now);
                if (_approved.Contains(Tuple.Create(documentKey, sessionId)))
                    return new ApprovalDecision(true, null);

                var pending = _pending.FirstOrDefault(request =>
                    request.DocumentKey == documentKey && request.SessionId == sessionId);
                if (pending == null)
                {
                    pending = new PendingApproval(documentKey, sessionId, clientName, CreateCode(), now);
                    _pending.Add(pending);
                }
                return new ApprovalDecision(false, pending.Code);
            }
        }

        public IReadOnlyList<PendingApproval> Pending
        {
            get
            {
                lock (_sync)
                {
                    ExpirePending(_utcNow());
                    // Immutable entries and a detached list prevent callers from mutating gate state.
                    return _pending.OrderBy(request => request.RequestedUtc).ToList().AsReadOnly();
                }
            }
        }

        public bool Approve(string code)
        {
            lock (_sync)
            {
                ExpirePending(_utcNow());
                var pending = _pending.FirstOrDefault(request => request.Code == code);
                if (pending == null)
                    return false;

                _approved.Add(Tuple.Create(pending.DocumentKey, pending.SessionId));
                _pending.Remove(pending);
                return true;
            }
        }

        public bool Deny(string code)
        {
            lock (_sync)
            {
                ExpirePending(_utcNow());
                return _pending.RemoveAll(request => request.Code == code) != 0;
            }
        }

        public void Revoke(string documentKey)
        {
            lock (_sync)
            {
                _approved.RemoveWhere(key => key.Item1 == documentKey);
                _pending.RemoveAll(request => request.DocumentKey == documentKey);
            }
        }

        public void DocumentChanged(string newDocumentKey)
        {
            lock (_sync)
            {
                _approved.RemoveWhere(key => key.Item1 != newDocumentKey);
                _pending.RemoveAll(request => request.DocumentKey != newDocumentKey);
            }
        }

        // Called only while holding _sync; expiration is inclusive at ten minutes.
        private void ExpirePending(DateTime now)
        {
            _pending.RemoveAll(request => now - request.RequestedUtc >= RequestLifetime);
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
        public ApprovalDecision(bool allowed, string code)
        {
            Allowed = allowed;
            Code = code;
            ErrorCode = allowed ? null : ErrorCodes.ApprovalRequired;
        }

        public bool Allowed { get; }
        public string Code { get; }
        public string ErrorCode { get; }
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
