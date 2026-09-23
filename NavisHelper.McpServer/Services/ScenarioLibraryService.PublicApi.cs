using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed partial class ScenarioLibraryService
{
    public ScenarioListResponse List(
        string query,
        string navisworksVersion,
        IReadOnlyCollection<string> rootFileNames,
        string projectLabel,
        int limit)
    {
        limit = Math.Clamp(limit, 1, 20);
        var response = new ScenarioListResponse
        {
            ScenarioDirectory = _rootPath,
            AllowedTools = ToolDescriptors.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
        };
        var records = ReadAll(response.Warnings);

        IEnumerable<ScenarioStoredRecord> filtered = records;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(record =>
                record.Scenario.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                (record.Scenario.Description ?? string.Empty).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        response.Scenarios = filtered
            .Select(record =>
            {
                var match = MatchContext(record.Scenario.Context, navisworksVersion, rootFileNames, projectLabel);
                return new ScenarioListItem
                {
                    ScenarioId = record.Scenario.ScenarioId,
                    Name = record.Scenario.Name,
                    Description = record.Scenario.Description,
                    ExecutionMode = record.Scenario.ExecutionMode,
                    UpdatedUtc = record.Scenario.UpdatedUtc,
                    Sha256 = record.Sha256,
                    MatchGrade = match.Grade,
                    MatchReasons = match.Reasons,
                };
            })
            .OrderBy(item => MatchRank(item.MatchGrade))
            .ThenByDescending(item => item.UpdatedUtc)
            .Take(limit)
            .ToList();
        response.Count = response.Scenarios.Count;
        return response;
    }

    public ScenarioGetResponse Get(string scenarioId)
    {
        var record = ReadById(scenarioId, out var errorCode, out var errorMessage);
        if (record == null)
        {
            return new ScenarioGetResponse
            {
                Ok = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            };
        }

        return new ScenarioGetResponse
        {
            Ok = true,
            Scenario = record.Scenario,
            Sha256 = record.Sha256,
            FilePath = record.FilePath,
            Warnings = record.Warnings,
        };
    }

    public ScenarioMutationResponse Save(
        ScenarioDraft draft,
        string scenarioId,
        string expectedSha256,
        bool apply,
        bool confirmSave,
        bool confirmExactReplay)
    {
        if (draft != null)
        {
            draft.Name = (draft.Name ?? string.Empty).Trim();
            draft.Description = (draft.Description ?? string.Empty).Trim();
        }
        var response = new ScenarioMutationResponse
        {
            Applied = false,
            ScenarioName = draft?.Name ?? string.Empty,
            ExecutionMode = draft?.ExecutionMode ?? string.Empty,
            AllowedTools = ToolDescriptors.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
        };

        var validation = ValidateDraft(draft, verifyExactFingerprint: false);
        if (validation.IsValid && IsExactReplay(draft))
        {
            draft.ExactReplay.SafetyEnvelope.PreviewFingerprint = "sha256:" + ComputeExactReplayFingerprint(draft);
            validation = ValidateDraft(draft, verifyExactFingerprint: true);
        }
        response.Errors.AddRange(validation.Errors);
        response.Warnings.AddRange(validation.Warnings);
        if (!validation.IsValid)
            return Fail(response, "scenario_invalid", "Сценарий не прошёл проверку.");

        var isUpdate = !string.IsNullOrWhiteSpace(scenarioId);
        ScenarioStoredRecord existing = null;
        if (isUpdate)
        {
            existing = ReadById(scenarioId, out var errorCode, out var errorMessage);
            if (existing == null)
                return Fail(response, errorCode, errorMessage);
            // Two different situations must not collapse into one message. A missing
            // expectedSha256 is an unfilled concurrency guard, not a changed scenario:
            // saying "changed after it was read" names a cause that did not happen, and
            // the advice to re-read and retry cannot help, because re-reading does not
            // supply a parameter. The message says only what is known -- and without the
            // caller's hash, whether the file changed is precisely what is not known.
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                return Fail(response, "scenario_conflict",
                    "Для обновления сценария укажите expectedSha256: его возвращают get_scenario и list_scenarios как поле sha256. " +
                    "Защита от одновременной правки не заполнена, поэтому изменился сценарий или нет -- неизвестно.");
            }
            if (!string.Equals(existing.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
            {
                return Fail(response, "scenario_conflict", "Сценарий изменился после чтения. Получите актуальную версию и повторите сохранение.");
            }
        }

        var duplicate = FindExactNameCollision(draft.Name, isUpdate ? existing.Scenario.ScenarioId : string.Empty);
        if (IsExactReplay(draft) && duplicate != null)
            return Fail(response, "scenario_name_conflict", "Точный сценарий с таким именем уже существует. Используйте уникальное имя.");

        var now = DateTime.UtcNow;
        var document = CopyDraft(draft);
        document.ScenarioId = existing?.Scenario.ScenarioId ?? Guid.NewGuid().ToString("D").ToLowerInvariant();
        document.CreatedUtc = existing?.Scenario.CreatedUtc ?? now;
        document.UpdatedUtc = now;
        response.ScenarioId = document.ScenarioId;
        response.UpdatedUtc = document.UpdatedUtc;
        response.FilePath = GetScenarioPath(document.ScenarioId);
        response.PlannedWrites.Add((isUpdate ? "replace " : "create ") + response.FilePath);

        if (!apply)
        {
            response.Ok = true;
            return response;
        }

        if (!confirmSave)
            return Fail(response, "scenario_save_confirmation_required", "Для записи сценария укажите confirm_save=true после проверки preview.");
        if (IsExactReplay(draft) && !confirmExactReplay)
            return Fail(response, "scenario_exact_replay_confirmation_required", "Для точного сценария требуется confirm_exact_replay=true.");

        lock (ProcessLock)
        {
            using var storeLease = AcquireStoreMutex();
            EnsureSafeRoot(create: true);
            if (!isUpdate && CountScenarioFiles() >= MaxScenarioFiles)
                return Fail(response, "scenario_store_full", "Достигнут лимит в 500 пользовательских сценариев.");

            duplicate = FindExactNameCollision(draft.Name, isUpdate ? existing.Scenario.ScenarioId : string.Empty);
            if (IsExactReplay(draft) && duplicate != null)
                return Fail(response, "scenario_name_conflict", "Точный сценарий с таким именем уже существует. Используйте уникальное имя.");

            if (isUpdate)
            {
                var current = ReadById(document.ScenarioId, out var errorCode, out var errorMessage);
                if (current == null)
                    return Fail(response, errorCode, errorMessage);
                if (!string.Equals(current.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
                    return Fail(response, "scenario_conflict", "Сценарий изменился перед записью. Изменения не применены.");
            }

            var json = JsonSerializer.Serialize(document, StoreJsonOptions) + Environment.NewLine;
            var byteCount = Encoding.UTF8.GetByteCount(json);
            if (byteCount > MaxScenarioFileBytes)
                return Fail(response, "scenario_too_large", "Размер сценария превышает 256 КиБ.");

            AtomicWrite(response.FilePath, json);
            response.Sha256 = ComputeSha256(Encoding.UTF8.GetBytes(json));
        }

        response.Ok = true;
        response.Applied = true;
        return response;
    }

    public ScenarioMutationResponse Delete(
        string scenarioId,
        string expectedSha256,
        bool apply,
        bool confirmDelete)
    {
        var response = new ScenarioMutationResponse { ScenarioId = scenarioId ?? string.Empty };
        var record = ReadById(scenarioId, out var errorCode, out var errorMessage);
        if (record == null)
            return Fail(response, errorCode, errorMessage);

        response.ScenarioName = record.Scenario.Name;
        response.ExecutionMode = record.Scenario.ExecutionMode;
        response.Sha256 = record.Sha256;
        response.FilePath = record.FilePath;
        response.UpdatedUtc = record.Scenario.UpdatedUtc;
        response.PlannedWrites.Add("delete " + record.FilePath);
        if (!apply)
        {
            response.Ok = true;
            return response;
        }

        if (!confirmDelete)
            return Fail(response, "scenario_delete_confirmation_required", "Для удаления сценария укажите confirm_delete=true после проверки preview.");
        // The same split as on save: without expectedSha256 the delete is refused because
        // the guard is unfilled, not because anyone changed the file.
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return Fail(response, "scenario_conflict",
                "Для удаления сценария укажите expectedSha256: его возвращают get_scenario и list_scenarios как поле sha256. " +
                "Защита от одновременной правки не заполнена, поэтому изменился сценарий или нет -- неизвестно.");
        }
        if (!string.Equals(record.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
        {
            return Fail(response, "scenario_conflict", "Сценарий изменился после чтения. Удаление отменено.");
        }

        lock (ProcessLock)
        {
            using var storeLease = AcquireStoreMutex();
            var current = ReadById(scenarioId, out errorCode, out errorMessage);
            if (current == null)
                return Fail(response, errorCode, errorMessage);
            if (!string.Equals(current.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
                return Fail(response, "scenario_conflict", "Сценарий изменился перед удалением. Удаление отменено.");

            File.Delete(current.FilePath);
        }

        response.Ok = true;
        response.Applied = true;
        return response;
    }

    private static ScenarioMutationResponse Fail(ScenarioMutationResponse response, string code, string message)
    {
        response.Ok = false;
        response.ErrorCode = code;
        response.ErrorMessage = message;
        return response;
    }
}
