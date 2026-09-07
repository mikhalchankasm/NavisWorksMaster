using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;

namespace NavisHelper.WPF
{
    internal sealed class UiThreadModelPreparationContext
    {
        private readonly Document _document;
        private readonly Action _checkCanContinue;
        private readonly Func<IEnumerable<ModelItem>, Action<ModelItem>, Task> _run;

        internal UiThreadModelPreparationContext(
            Document document,
            Action checkCanContinue,
            Func<IEnumerable<ModelItem>, Action<ModelItem>, Task> run)
        {
            _document = document;
            _checkCanContinue = checkCanContinue;
            _run = run;
        }

        internal async Task<ModelItemCollection> CopySelectionAsync()
        {
            _checkCanContinue();
            var source = _document.CurrentSelection.SelectedItems;
            var result = new ModelItemCollection();
            await _run(source, item => { if (item != null) result.Add(item); });
            return result;
        }

        internal async Task<HashSet<ModelItem>> BuildSelectionSetAsync(ModelItemCollection source)
        {
            var result = new HashSet<ModelItem>();
            await _run(source, item => { if (item != null) result.Add(item); });
            return result;
        }

        internal async Task<HashSet<ModelItem>> CollectAncestorsAsync(ModelItemCollection source)
        {
            var result = new HashSet<ModelItem>();
            // Each parent step is one work item, including on deep trees.
            await _run(EnumerateAncestors(source), item =>
            {
                if (item != null) result.Add(item);
            });
            return result;
        }

        private static IEnumerable<ModelItem> EnumerateAncestors(ModelItemCollection source)
        {
            foreach (var item in source)
            {
                // Even a parentless source consumes a slice item.
                var parent = item?.Parent;
                yield return parent;
                while (parent != null)
                {
                    parent = parent.Parent;
                    yield return parent;
                }
            }
        }
    }
}
