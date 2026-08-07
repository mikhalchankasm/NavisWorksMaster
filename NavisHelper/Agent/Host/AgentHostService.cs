using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ApplicationParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Agent.Session;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService : IDisposable
    {
        private const int ListenerSlots = 2;
        private const int MaxFrameLengthBytes = ProtocolConstants.MaxFrameLengthBytes;
        private const int ConnectionIdleTimeoutMs = 10000;
        private const int DeferredGateReleaseGraceMs = 5000;
        private const int MaxOperationHistoryCount = 128;
        private static readonly JsonSerializerSettings JsonSettings = CreateJsonSettings();
        private static readonly JsonSerializer JsonDeserializer = JsonSerializer.Create(JsonSettings);

        private readonly object _listenerSync = new object();
        private readonly List<NamedPipeServerStream> _listeners = new List<NamedPipeServerStream>();
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private readonly SearchService _searchService = new SearchService();
        private readonly DocumentCommandService _commandService = new DocumentCommandService();
        private readonly ClashIsolationService _clashIsolationService = new ClashIsolationService();
        private readonly ModelColorSchemeService _modelColorSchemeService = new ModelColorSchemeService();
        private readonly SectionBoxCaptureService _sectionBoxCaptureService = new SectionBoxCaptureService();
        private readonly BoxIsolationService _boxIsolationService = new BoxIsolationService();
        private readonly ClashTestsFromSetsService _clashTestsFromSetsService = new ClashTestsFromSetsService();
        private readonly ClashBatchRunService _clashBatchRunService;
        private readonly NavisworksApplicationCloseService _applicationCloseService;
        private readonly MatchSessionStore _matchSessionStore = new MatchSessionStore();
        private readonly CommandRouter _commandRouter;
        private readonly object _operationHistorySync = new object();
        private readonly Dictionary<string, OperationRecord> _operationHistory = new Dictionary<string, OperationRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _operationHistoryOrder = new Queue<string>();

        private SynchronizationContext _uiContext;
        private Control _uiControl;
        private CancellationTokenSource _shutdownCts;
        private string _instanceId;
        private string _pipeName;
        private string _discoveryFilePath;
        private DateTime _startedAtUtc;
        private DateTime? _processStartedAtUtc;
        private bool _isStarted;
        private Document _trackedDocument;
        private string _lastDocumentFileName = string.Empty;
        private string _lastDiscoveryDocumentTitle = string.Empty;

        internal AgentHostService()
        {
            _applicationCloseService = new NavisworksApplicationCloseService(_commandService);
            _clashBatchRunService = new ClashBatchRunService(PostToUi);
            _commandRouter = CreateCommandRouter();
        }
    }
}
