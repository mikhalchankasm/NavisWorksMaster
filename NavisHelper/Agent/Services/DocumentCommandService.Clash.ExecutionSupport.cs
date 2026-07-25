using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {

        private static int RunClashTestsWithProgress(
            DocumentClashTests testsData,
            IList<ClashTest> tests,
            string title,
            Action<ClashTest> onRan,
            Action<ClashTest, Exception> onFailed,
            Action onCancelled = null,
            Func<bool> isCancellationRequested = null)
        {
            if (testsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");
            if (tests == null || tests.Count == 0)
                return 0;

            var ran = 0;
            var progress = Autodesk.Navisworks.Api.Application.BeginProgress(
                string.IsNullOrWhiteSpace(title) ? "NavisHelper: run Clash tests" : title,
                "0 / " + tests.Count.ToString(CultureInfo.InvariantCulture));
            ScreenUpdateSuppressor screenUpdates = null;
            try
            {
                screenUpdates = ScreenUpdateSuppressor.TryDisable();
                for (var i = 0; i < tests.Count; i++)
                {
                    if (progress.IsCanceled || (isCancellationRequested != null && isCancellationRequested()))
                    {
                        if (onCancelled != null)
                            onCancelled();
                        break;
                    }

                    var test = tests[i];
                    progress.Update(tests.Count == 0 ? 1.0 : (double)i / tests.Count);
                    if (test == null)
                        continue;

                    try
                    {
                        ClashRunPreservationService.RunTestPreservingReviewState(testsData, test);
                        ran++;
                        if (onRan != null)
                            onRan(test);
                    }
                    catch (Exception ex)
                    {
                        if (onFailed != null)
                            onFailed(test, ex);
                    }

                    progress.Update((double)(i + 1) / tests.Count);
                }
            }
            finally
            {
                if (screenUpdates != null)
                    screenUpdates.Dispose();
                Autodesk.Navisworks.Api.Application.EndProgress();
            }

            return ran;
        }

        private static int RunClashTestsByNameWithProgress(
            DocumentClash clash,
            IList<string> testNames,
            string title,
            Action<string> onRan,
            Action<string, Exception> onFailed,
            Action onCancelled = null)
        {
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var names = testNames == null
                ? new List<string>()
                : testNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0)
                return 0;

            var ran = 0;
            var progress = Autodesk.Navisworks.Api.Application.BeginProgress(
                string.IsNullOrWhiteSpace(title) ? "NavisHelper: run Clash tests" : title,
                "0 / " + names.Count.ToString(CultureInfo.InvariantCulture));
            ScreenUpdateSuppressor screenUpdates = null;
            try
            {
                screenUpdates = ScreenUpdateSuppressor.TryDisable();
                for (var i = 0; i < names.Count; i++)
                {
                    if (progress.IsCanceled)
                    {
                        if (onCancelled != null)
                            onCancelled();
                        break;
                    }

                    var testName = names[i];
                    progress.Update((double)i / names.Count);
                    try
                    {
                        var test = FindClashTestByName(ClashApiCompat.GetClashTests(clash).ToList(), testName);
                        if (test == null)
                            throw new AgentCommandException(ErrorCodes.CommandFailed, "Created Clash Detective test could not be resolved for run: " + testName);

                        ClashRunPreservationService.RunTestPreservingReviewState(clash.TestsData, test);
                        ran++;
                        if (onRan != null)
                            onRan(testName);
                    }
                    catch (Exception ex)
                    {
                        if (onFailed != null)
                            onFailed(testName, ex);
                    }

                    progress.Update((double)(i + 1) / names.Count);
                }
            }
            finally
            {
                if (screenUpdates != null)
                    screenUpdates.Dispose();
                Autodesk.Navisworks.Api.Application.EndProgress();
            }

            return ran;
        }













        private ClashReportOperationState StartClashReportOperation(string outputDirectory, string reportPath, string manifestPath, int resultOffset, int totalBatchCount)
        {
            try
            {
                return _clashReportOperations.Start(outputDirectory, reportPath, manifestPath, resultOffset, totalBatchCount);
            }
            catch (InvalidOperationException ex)
            {
                throw new AgentCommandException(ErrorCodes.HostBusy, ex.Message);
            }
        }

        private bool IsClashReportCancellationRequested(ClashReportOperationState operation)
        {
            return _clashReportOperations.IsCancellationRequested(operation);
        }

        private void UpdateClashReportOperationState(ClashReportOperationState operation, string state, string message)
        {
            _clashReportOperations.UpdateState(operation, state, message);
        }

        private void UpdateClashReportOperationBatchScope(ClashReportOperationState operation, int totalBatchCount)
        {
            _clashReportOperations.UpdateBatchScope(operation, totalBatchCount);
        }

        private void UpdateClashReportOperationCurrent(ClashReportOperationState operation, ClashReportWorkItem row, int globalIndex)
        {
            _clashReportOperations.UpdateCurrent(
                operation,
                row == null ? string.Empty : row.TestName,
                row == null ? string.Empty : row.ResultName,
                globalIndex);
        }

        private void UpdateClashReportOperationProgress(ClashReportOperationState operation, ClashGenerateReportResponse response)
        {
            _clashReportOperations.UpdateProgress(
                operation,
                response == null || response.Items == null ? 0 : response.Items.Count,
                response == null ? 0 : response.CreatedViewpointCount,
                response == null ? 0 : response.ScreenshotCount);
        }

        private void CompleteClashReportOperation(ClashReportOperationState operation, bool cancelled, string message)
        {
            _clashReportOperations.Complete(operation, cancelled, message);
        }

        private void FailClashReportOperation(ClashReportOperationState operation, string message)
        {
            _clashReportOperations.Fail(operation, message);
        }
    }
}