using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService
    {
        private readonly ClashTestTransferExporterService _clashTransferExporterService = new ClashTestTransferExporterService();
        private ClashBatchtestImportService _clashBatchtestImportService;

        private void RegisterClashTransferCommands(CommandRouter router)
        {
            router.Register<ClashTestsExportRequest>(
                HostCommandNames.ClashTestsExport,
                true,
                DeserializePayload<ClashTestsExportRequest>,
                (document, request) => _clashTransferExporterService.Execute(document, request));

            router.Register<ClashBatchtestImportRequest>(
                HostCommandNames.ClashBatchtestImport,
                true,
                DeserializePayload<ClashBatchtestImportRequest>,
                (document, request) => ImportClashBatchtest(document, request));
        }

        private ClashBatchtestImportResponse ImportClashBatchtest(Document document, ClashBatchtestImportRequest request)
        {
            if (_clashBatchtestImportService == null)
                _clashBatchtestImportService = new ClashBatchtestImportService(_clashTestsFromSetsService);
            return _clashBatchtestImportService.Execute(document, request);
        }
    }
}
