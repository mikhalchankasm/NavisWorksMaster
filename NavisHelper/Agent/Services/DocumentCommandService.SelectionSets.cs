using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public CreateSearchSetResponse CreateSearchSet(Document document, CreateSearchSetRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SelectionSets == null || document.SelectionSets.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection sets collection.");

            var apply = request.Apply == true;
            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Search set name is required.");

            var conditions = (request.Conditions ?? new List<FindItemsCondition>())
                .Select(NormalizeSelectionSetCondition)
                .ToList();
            if (conditions.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "At least one search condition is required.");

            var combineOperator = NormalizeSelectionSetCombineOperator(request.CombineOperator);
            if (!string.Equals(combineOperator, FindItemsCombineOperators.All, StringComparison.OrdinalIgnoreCase))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "create_search_set currently supports combine_operator=all only.");

            var folderPath = NormalizeFolderPath(request.FolderPath);
            GroupItem targetFolder;
            bool folderExists;
            int createdFolderCount;
            if (!TryResolveSelectionSetFolder(document.SelectionSets, folderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = document.SelectionSets.RootItem;
            var targetFolderExistedBeforeApply = folderExists;

            var existing = folderExists && targetFolder != null ? FindChildSavedItemByName(targetFolder, name) : null;
            var nameConflict = existing != null;
            if (apply && nameConflict && request.Overwrite != true)
                throw new AgentCommandException(ErrorCodes.SelectionSetNameConflict, "A selection/search set or folder with the same name already exists in the target folder.");
            if (apply && existing != null && !(existing is SelectionSet))
                throw new AgentCommandException(ErrorCodes.SelectionSetNameConflict, "A folder or unsupported saved item with the same name already exists in the target folder.");

            var bindingWarnings = new List<string>();
            var runtimeResolvedConditionCount = 0;
            var search = SelectionSetSearchBuilder.Build(document, conditions, bindingWarnings, ref runtimeResolvedConditionCount);
            var matchedItems = search.FindAll(document, false);

            if (apply)
            {
                if (!TryResolveSelectionSetFolder(document.SelectionSets, folderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target selection sets folder.");

                existing = FindChildSavedItemByName(targetFolder, name);
                var searchSet = new SelectionSet(search)
                {
                    DisplayName = name,
                };

                if (existing != null)
                {
                    if (!(existing is SelectionSet))
                        throw new AgentCommandException(ErrorCodes.SelectionSetNameConflict, "A folder or unsupported saved item with the same name already exists in the target folder.");

                    var existingIndex = targetFolder.Children.IndexOf(existing);
                    if (existingIndex < 0)
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine existing selection set index.");

                    document.SelectionSets.ReplaceWithCopy(targetFolder, existingIndex, searchSet);
                }
                else
                {
                    document.SelectionSets.InsertCopy(targetFolder, targetFolder.Children.Count, searchSet);
                }

                if (request.SelectAfterCreate == true)
                {
                    document.CurrentSelection.Clear();
                    document.CurrentSelection.CopyFrom(matchedItems);
                }
            }

            var path = string.IsNullOrWhiteSpace(folderPath) ? name : folderPath + "/" + name;
            var response = new CreateSearchSetResponse
            {
                Apply = apply,
                Name = name,
                FolderPath = folderPath,
                Path = path,
                ConditionCount = conditions.Count,
                MatchedItemCount = matchedItems.Count,
                NameConflict = nameConflict,
                FolderExists = targetFolderExistedBeforeApply,
                CreatedFolderCount = apply ? (int?)createdFolderCount : null,
                Created = apply ? (bool?)(existing == null) : null,
                Overwritten = apply ? (bool?)(existing != null) : null,
                Selected = apply && request.SelectAfterCreate == true ? (bool?)true : null,
                RuntimeResolvedConditionCount = runtimeResolvedConditionCount,
                Warnings = bindingWarnings,
            };
            if (matchedItems.Count == 0)
                response.Warnings.Add("Search matched 0 items. The dynamic search set can still be created, but verify property names and values.");
            if (matchedItems.Count == 0 && conditions.Any(UsesInternalPropertyOnly))
                response.Warnings.Add("Search matched 0 items while using internal property names only. Internal IDs can change between model builds; prefer display category/property names when available.");
            if (conditions.Any(HasDisplayAndInternalPropertyNames))
                response.Warnings.Add("Display category/property names were used for persisted conditions; supplied internal IDs were ignored to keep the Search Set portable between model builds.");
            if (nameConflict && request.Overwrite != true)
                response.Warnings.Add("A saved item with the same name already exists in the target folder; apply=true will fail unless overwrite=true.");
            return response;
        }

        private static bool UsesInternalPropertyOnly(FindItemsCondition condition)
        {
            return SearchSetConditionRulesHelper.UsesInternalPropertyOnly(condition);
        }

        private static bool HasDisplayAndInternalPropertyNames(FindItemsCondition condition)
        {
            return SearchSetConditionRulesHelper.HasDisplayAndInternalPropertyNames(condition);
        }

        private static FindItemsCondition NormalizeSelectionSetCondition(FindItemsCondition condition)
        {
            try
            {
                return SearchSetConditionRulesHelper.NormalizeCondition(condition);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }

        private static string NormalizeSelectionSetCombineOperator(string combineOperator)
        {
            try
            {
                return SearchSetConditionRulesHelper.NormalizeCombineOperator(combineOperator);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }

    }
}
