using System;

namespace NavisHelper.Core.Localization
{
    internal sealed class UiLanguageRadioState
    {
        private bool _isInitializing = true;
        private bool _isProgrammaticRefresh;

        internal UiLanguageRadioState(UiLanguage currentLanguage)
        {
            SelectedLanguage = currentLanguage;
        }

        internal UiLanguage SelectedLanguage { get; private set; }

        internal bool IsRussianChecked => SelectedLanguage == UiLanguage.Russian;

        internal bool IsEnglishChecked => SelectedLanguage == UiLanguage.English;

        internal void CompleteInitialization()
        {
            _isInitializing = false;
        }

        internal void Refresh(
            UiLanguage currentLanguage,
            Action<bool, bool> applyCheckedState)
        {
            if (applyCheckedState == null)
                throw new ArgumentNullException(nameof(applyCheckedState));

            bool wasProgrammaticRefresh = _isProgrammaticRefresh;
            _isProgrammaticRefresh = true;
            SelectedLanguage = currentLanguage;
            try
            {
                applyCheckedState(IsRussianChecked, IsEnglishChecked);
            }
            finally
            {
                _isProgrammaticRefresh = wasProgrammaticRefresh;
            }
        }

        internal bool TrySelect(
            UiLanguage selectedLanguage,
            Func<UiLanguage, bool> setManualLanguage,
            out bool persisted)
        {
            persisted = false;
            if (_isInitializing ||
                _isProgrammaticRefresh ||
                selectedLanguage == SelectedLanguage ||
                setManualLanguage == null)
                return false;

            UiLanguage previousLanguage = SelectedLanguage;
            SelectedLanguage = selectedLanguage;
            try
            {
                persisted = setManualLanguage(selectedLanguage);
                return true;
            }
            catch
            {
                SelectedLanguage = previousLanguage;
                throw;
            }
        }
    }
}
