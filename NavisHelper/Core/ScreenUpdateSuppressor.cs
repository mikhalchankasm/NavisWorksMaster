using System;
using System.Reflection;
using Autodesk.Navisworks.Api.ComApi;

namespace NavisHelper.Core
{
    internal sealed class ScreenUpdateSuppressor : IDisposable
    {
        private readonly object _state;
        private readonly PropertyInfo _property;

        public bool IsActive { get; private set; }
        public string Status { get; private set; }

        private ScreenUpdateSuppressor(object state, PropertyInfo property, bool isActive, string status)
        {
            _state = state;
            _property = property;
            IsActive = isActive;
            Status = status ?? string.Empty;
        }

        public static ScreenUpdateSuppressor TryDisable()
        {
            try
            {
                var state = ComApiBridge.State;
                if (state == null)
                    return new ScreenUpdateSuppressor(null, null, false, "COM state unavailable");

                var property = state.GetType().GetProperty("DisableScreenUpdates");
                if (property == null || !property.CanWrite)
                    return new ScreenUpdateSuppressor(null, null, false, "DisableScreenUpdates unavailable");

                property.SetValue(state, true, null);
                return new ScreenUpdateSuppressor(state, property, true, "DisableScreenUpdates enabled");
            }
            catch (Exception ex)
            {
                return new ScreenUpdateSuppressor(null, null, false, "DisableScreenUpdates failed: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (!IsActive || _state == null || _property == null)
                return;

            try
            {
                _property.SetValue(_state, false, null);
                Status += "; restored";
            }
            catch (Exception ex)
            {
                Status += "; restore failed: " + ex.Message;
            }
            finally
            {
                IsActive = false;
            }
        }
    }
}
