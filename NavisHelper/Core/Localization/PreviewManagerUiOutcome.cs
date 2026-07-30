using System;

namespace NavisHelper.Core.Localization
{
    internal enum PreviewManagerUiOutcomeKind
    {
        None = 0,
        SelectionNoActiveDocument,
        SelectionBoundsUnavailable,
        SelectionNoSelectionOrSavedBox,
        SelectionApplied,
        SelectionAppliedFromSavedAnchor,
        SelectionReset,
        ClashPairRestoreFailed,
        ClashResultNoItems,
        ClashResultShown,
        ClashGroupEmpty,
        ClashGroupNoItems,
        ClashGroupNoItemsUnnamed,
        ClashGroupShown,
        ClashGroupShownUnnamed,
        PairIsolationRestoreFailed,
        PairIsolationSkippedInactiveModel,
        PairIsolationApplied,
        TransparencyNoActiveDocument,
        TransparencyZeroLevel,
        TransparencyClashItemsMissing,
        TransparencyOwnersMissing,
        TransparencyApplied
    }

    internal sealed class PreviewManagerUiOutcome
    {
        private static readonly PreviewManagerUiOutcome Empty =
            new PreviewManagerUiOutcome(PreviewManagerUiOutcomeKind.None);

        internal PreviewManagerUiOutcome(
            PreviewManagerUiOutcomeKind kind,
            params object[] arguments)
        {
            Kind = kind;
            Arguments = arguments ?? new object[0];
        }

        internal PreviewManagerUiOutcomeKind Kind { get; }

        internal object[] Arguments { get; }

        internal static PreviewManagerUiOutcome None => Empty;
    }

    internal sealed class UiStatusResourceDescriptor
    {
        internal UiStatusResourceDescriptor(string resourceKey, params object[] arguments)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                throw new ArgumentException("A semantic resource key is required.", nameof(resourceKey));

            ResourceKey = resourceKey;
            Arguments = arguments ?? new object[0];
        }

        internal string ResourceKey { get; }

        internal object[] Arguments { get; }

        internal UiLocalizedArgument AsLocalizedArgument()
        {
            return UiLocalizedArgument.FromResource(ResourceKey, Arguments);
        }
    }

    internal static class PreviewManagerUiStatusMapper
    {
        internal static UiStatusResourceDescriptor ForSelection(PreviewManagerUiOutcome outcome)
        {
            outcome = outcome ?? PreviewManagerUiOutcome.None;
            switch (outcome.Kind)
            {
                case PreviewManagerUiOutcomeKind.SelectionNoActiveDocument:
                    return new UiStatusResourceDescriptor("Panel_Common_NoActiveDocument");
                case PreviewManagerUiOutcomeKind.SelectionBoundsUnavailable:
                    return new UiStatusResourceDescriptor("Panel_Selection_BoundsUnavailable");
                case PreviewManagerUiOutcomeKind.SelectionNoSelectionOrSavedBox:
                    return new UiStatusResourceDescriptor("Panel_Selection_NoSelectionOrSavedBox");
                case PreviewManagerUiOutcomeKind.SelectionApplied:
                    return new UiStatusResourceDescriptor(
                        "Panel_Selection_SectionBoxApplied_Format",
                        outcome.Arguments);
                case PreviewManagerUiOutcomeKind.SelectionAppliedFromSavedAnchor:
                    return new UiStatusResourceDescriptor(
                        "Panel_Selection_SectionBoxAppliedSaved_Format",
                        outcome.Arguments);
                case PreviewManagerUiOutcomeKind.SelectionReset:
                    return new UiStatusResourceDescriptor("Panel_Selection_SectionBoxReset");
                default:
                    return new UiStatusResourceDescriptor("Panel_Status_Ready");
            }
        }

        internal static UiStatusResourceDescriptor ForClashPreview(PreviewManagerUiOutcome outcome)
        {
            outcome = outcome ?? PreviewManagerUiOutcome.None;
            switch (outcome.Kind)
            {
                case PreviewManagerUiOutcomeKind.ClashPairRestoreFailed:
                    return new UiStatusResourceDescriptor("Panel_Clash_Preview_RestoreFailed");
                case PreviewManagerUiOutcomeKind.ClashResultNoItems:
                case PreviewManagerUiOutcomeKind.ClashGroupNoItems:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Preview_NoItems_Format",
                        outcome.Arguments);
                case PreviewManagerUiOutcomeKind.ClashGroupNoItemsUnnamed:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Preview_NoItems_Format",
                        UiLocalizedArgument.FromResource(
                            "Panel_Clash_Preview_DefaultGroupName"));
                case PreviewManagerUiOutcomeKind.ClashGroupEmpty:
                    return new UiStatusResourceDescriptor("Panel_Clash_Preview_EmptyGroup");
                case PreviewManagerUiOutcomeKind.ClashResultShown:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Preview_ResultShown_Format",
                        outcome.Arguments.Length > 0 ? outcome.Arguments[0] : string.Empty);
                case PreviewManagerUiOutcomeKind.ClashGroupShown:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Preview_GroupShown_Format",
                        outcome.Arguments.Length > 0 ? outcome.Arguments[0] : string.Empty,
                        outcome.Arguments.Length > 1 ? outcome.Arguments[1] : 0);
                case PreviewManagerUiOutcomeKind.ClashGroupShownUnnamed:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Preview_GroupShown_Format",
                        UiLocalizedArgument.FromResource(
                            "Panel_Clash_Preview_DefaultGroupName"),
                        outcome.Arguments.Length > 0 ? outcome.Arguments[0] : 0);
                default:
                    return new UiStatusResourceDescriptor("Panel_Clash_Preview_Failed");
            }
        }

        internal static UiStatusResourceDescriptor ForPairIsolation(PreviewManagerUiOutcome outcome)
        {
            outcome = outcome ?? PreviewManagerUiOutcome.None;
            switch (outcome.Kind)
            {
                case PreviewManagerUiOutcomeKind.PairIsolationRestoreFailed:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_PairIsolation_RestoreFailed_Format",
                        outcome.Arguments);
                case PreviewManagerUiOutcomeKind.PairIsolationSkippedInactiveModel:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_PairIsolation_SkippedInactiveModel");
                case PreviewManagerUiOutcomeKind.PairIsolationApplied:
                    return outcome.Arguments.Length > 1 &&
                           Convert.ToInt32(outcome.Arguments[1]) > 0
                        ? new UiStatusResourceDescriptor(
                            "Panel_Clash_PairIsolation_AppliedWithProxy_Format",
                            outcome.Arguments)
                        : new UiStatusResourceDescriptor(
                            "Panel_Clash_Preview_HiddenBranches_Format",
                            outcome.Arguments.Length > 0 ? outcome.Arguments[0] : 0);
                default:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Preview_HiddenBranches_Format",
                        0);
            }
        }

        internal static UiStatusResourceDescriptor ForTransparencyDetails(
            PreviewManagerUiOutcome outcome)
        {
            outcome = outcome ?? PreviewManagerUiOutcome.None;
            switch (outcome.Kind)
            {
                case PreviewManagerUiOutcomeKind.TransparencyNoActiveDocument:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Transparency_NoActiveDocument_Detail");
                case PreviewManagerUiOutcomeKind.TransparencyZeroLevel:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Transparency_ZeroLevel_Detail");
                case PreviewManagerUiOutcomeKind.TransparencyClashItemsMissing:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Transparency_ClashItemsMissing_Detail");
                case PreviewManagerUiOutcomeKind.TransparencyOwnersMissing:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Transparency_OwnersMissing_Detail");
                case PreviewManagerUiOutcomeKind.TransparencyApplied:
                    return new UiStatusResourceDescriptor(
                        "Panel_Clash_Transparency_Applied_Detail_Format",
                        outcome.Arguments);
                default:
                    return null;
            }
        }
    }
}
