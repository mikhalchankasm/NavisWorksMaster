using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using Autodesk.Navisworks.Api.Plugins;

namespace NavisHelper.PanelLifecycleSmoke
{
    [Plugin("NavisHelperPanelLifecycleSmoke", "CODX", DisplayName = "NavisHelper Panel Lifecycle Smoke")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class PanelLifecycleSmokePlugin : AddInPlugin
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        public override int Execute(params string[] parameters)
        {
            var outputPath = parameters != null && parameters.Length > 0
                ? parameters[0]
                : Path.Combine(Path.GetTempPath(), "navishelper-panel-lifecycle-smoke.txt");

            try
            {
                var panelType = LoadPanelType();
                Require(GetCurrent(panelType) == null, "NavisHelperPanel.Current must be null before isolated smoke");

                var first = RunCycle(panelType, true);
                var second = RunCycle(panelType, false);
                Require(GetCurrent(panelType) == null, "NavisHelperPanel.Current remained set after second unload");
                Require(GetStaticField(panelType, "_hook") == null, "Keyboard hook remained set after second unload");
                Require(GetStaticField(panelType, "_hookOwner") == null, "Keyboard hook owner remained set after second unload");

                WriteResult(outputPath, new[]
                {
                    "status=passed",
                    "cycle1=" + first,
                    "cycle2=" + second,
                    "current_after_unload=null",
                    "hook_after_unload=null",
                    "hook_owner_after_unload=null"
                });
                return 0;
            }
            catch (Exception ex)
            {
                WriteResult(outputPath, new[]
                {
                    "status=failed",
                    "error=" + ex
                });
                return 1;
            }
        }

        private static Type LoadPanelType()
        {
            var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("NavisHelperDockPane.CBC");
            Require(record != null, "NavisHelperDockPane.CBC was not registered");
            if (!record.IsLoaded)
                record.LoadPlugin();

            Require(record.LoadedPlugin != null, "NavisHelperDockPane.CBC did not load");
            var panelType = record.LoadedPlugin.GetType().Assembly.GetType("NavisHelper.WPF.NavisHelperPanel", false);
            Require(panelType != null, "NavisHelper.WPF.NavisHelperPanel type was not found");
            return panelType;
        }

        private static string RunCycle(Type panelType, bool exercisePalette)
        {
            Form form = null;
            ElementHost host = null;
            object panel = null;
            try
            {
                panel = Activator.CreateInstance(panelType);
                var panelElement = panel as UIElement;
                Require(panelElement != null, "NavisHelperPanel is not a UIElement");

                host = new ElementHost
                {
                    Dock = DockStyle.Fill,
                    Child = panelElement
                };
                form = new Form
                {
                    Width = 420,
                    Height = 700,
                    Left = -32000,
                    Top = -32000,
                    ShowInTaskbar = false,
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    Opacity = 0.01
                };
                form.Controls.Add(host);
                host.CreateControl();
                form.Show();

                Require(WaitUntil(() => ReferenceEquals(GetCurrent(panelType), panel), 3000),
                    "Loaded did not assign NavisHelperPanel.Current");
                Require(GetStaticField(panelType, "_hook") != null, "Loaded did not install the keyboard hook");
                Require(ReferenceEquals(GetStaticField(panelType, "_hookOwner"), panel),
                    "Loaded did not assign keyboard hook ownership");

                var tabControl = FindDescendant<System.Windows.Controls.TabControl>(panelElement);
                Require(tabControl != null, "TabControl was not created");
                Require(tabControl.Items.Count == 6, "Expected 6 tabs, found " + tabControl.Items.Count);
                var headers = tabControl.Items.Cast<object>()
                    .Select(item => ((System.Windows.Controls.TabItem)item).Header == null
                        ? string.Empty
                        : ((System.Windows.Controls.TabItem)item).Header.ToString())
                    .ToArray();
                Require(headers.All(header => !string.IsNullOrWhiteSpace(header)), "One or more tab headers are blank");

                StartPrivateTimer(panelType, panel, "_selectionSectionDebounceTimer");
                StartPrivateTimer(panelType, panel, "_clashPreviewDebounceTimer");
                StartPrivateTimer(panelType, panel, "_clashDataChangedDebounceTimer");

                if (exercisePalette)
                {
                    InvokePrivate(panelType, panel, "OpenCommandPalette");
                    Require(WaitUntil(() =>
                    {
                        var window = GetInstanceField(panelType, panel, "_commandPaletteWindow") as Window;
                        return window != null && window.IsVisible;
                    }, 3000), "Command palette did not open");
                }

                host.Child = null;
                Require(WaitUntil(() => GetCurrent(panelType) == null, 3000),
                    "Unloaded did not clear NavisHelperPanel.Current");
                Require(GetStaticField(panelType, "_hook") == null, "Unloaded did not dispose the keyboard hook");
                Require(GetStaticField(panelType, "_hookOwner") == null, "Unloaded did not clear keyboard hook ownership");
                Require(GetInstanceField(panelType, panel, "_commandPaletteWindow") == null,
                    "Unloaded did not close the command palette");
                RequirePrivateTimerStopped(panelType, panel, "_selectionSectionDebounceTimer");
                RequirePrivateTimerStopped(panelType, panel, "_clashPreviewDebounceTimer");
                RequirePrivateTimerStopped(panelType, panel, "_clashDataChangedDebounceTimer");

                return "tabs=" + string.Join("|", headers) + ",palette=" + (exercisePalette ? "opened_and_closed" : "not_opened");
            }
            finally
            {
                if (host != null)
                    host.Child = null;
                if (form != null)
                {
                    form.Close();
                    form.Dispose();
                }
                if (host != null)
                    host.Dispose();
                PumpMessages();
            }
        }

        private static void StartPrivateTimer(Type panelType, object panel, string fieldName)
        {
            var field = RequireField(panelType, fieldName, InstancePrivate);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            field.SetValue(panel, timer);
            timer.Start();
            Require(timer.IsEnabled, fieldName + " did not start");
        }

        private static void RequirePrivateTimerStopped(Type panelType, object panel, string fieldName)
        {
            var timer = GetInstanceField(panelType, panel, fieldName) as DispatcherTimer;
            Require(timer != null && !timer.IsEnabled, fieldName + " remained enabled after unload");
        }

        private static object GetCurrent(Type panelType)
        {
            var property = panelType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            Require(property != null, "NavisHelperPanel.Current property was not found");
            return property.GetValue(null, null);
        }

        private static object GetStaticField(Type panelType, string name)
        {
            return RequireField(panelType, name, StaticPrivate).GetValue(null);
        }

        private static object GetInstanceField(Type panelType, object instance, string name)
        {
            return RequireField(panelType, name, InstancePrivate).GetValue(instance);
        }

        private static FieldInfo RequireField(Type panelType, string name, BindingFlags flags)
        {
            var field = panelType.GetField(name, flags);
            Require(field != null, name + " field was not found");
            return field;
        }

        private static void InvokePrivate(Type panelType, object instance, string name)
        {
            var method = panelType.GetMethod(name, InstancePrivate);
            Require(method != null, name + " method was not found");
            method.Invoke(instance, null);
        }

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
                return null;
            if (root is T match)
                return match;

            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                var found = FindDescendant<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static bool WaitUntil(Func<bool> predicate, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                PumpMessages();
                if (predicate())
                    return true;
                Thread.Sleep(25);
            }
            while (DateTime.UtcNow < deadline);
            PumpMessages();
            return predicate();
        }

        private static void PumpMessages()
        {
            System.Windows.Forms.Application.DoEvents();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new DispatcherOperationCallback(state =>
                {
                    ((DispatcherFrame)state).Continue = false;
                    return null;
                }),
                frame);
            Dispatcher.PushFrame(frame);
        }

        private static void WriteResult(string outputPath, IEnumerable<string> lines)
        {
            try
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllLines(outputPath, lines);
            }
            catch
            {
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
