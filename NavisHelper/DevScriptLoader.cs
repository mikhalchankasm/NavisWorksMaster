using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NavisHelper.Interfaces;

namespace NavisHelper
{
    /// <summary>
    /// Загрузчик тестовых скриптов из внешней DLL.
    /// Использует Assembly.Load(byte[]) — не блокирует файл, позволяет перезагрузку.
    /// </summary>
    public static class DevScriptLoader
    {
        /// <summary>Путь к последней загруженной DLL.</summary>
        public static string LastLoadedPath { get; private set; }

        /// <summary>Время последней загрузки.</summary>
        public static DateTime? LastLoadedTime { get; private set; }

        /// <summary>Версия загруженной DLL.</summary>
        public static string LastLoadedVersion { get; private set; }

        /// <summary>Время модификации файла DLL на диске.</summary>
        public static DateTime? LastFileTime { get; private set; }

        /// <summary>Список скриптов из последней загрузки.</summary>
        public static List<IDevScript> LoadedScripts { get; private set; } = new List<IDevScript>();

        /// <summary>
        /// Загружает DLL и находит все классы, реализующие IDevScript.
        /// </summary>
        /// <param name="dllPath">Путь к DLL файлу.</param>
        /// <returns>Список найденных скриптов.</returns>
        public static List<IDevScript> LoadScripts(string dllPath)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException("DLL не найдена: " + dllPath);

            // Читаем байты — файл не блокируется, можно перекомпилировать
            byte[] dllBytes = File.ReadAllBytes(dllPath);

            // Пробуем загрузить PDB для отладки
            byte[] pdbBytes = null;
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (File.Exists(pdbPath))
                pdbBytes = File.ReadAllBytes(pdbPath);

            Assembly assembly = pdbBytes != null
                ? Assembly.Load(dllBytes, pdbBytes)
                : Assembly.Load(dllBytes);

            var scripts = new List<IDevScript>();

            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (typeof(IDevScript).IsAssignableFrom(type))
                {
                    var instance = (IDevScript)Activator.CreateInstance(type);
                    scripts.Add(instance);
                }
            }

            if (scripts.Count == 0)
                throw new InvalidOperationException(
                    "В DLL не найдено классов, реализующих IDevScript.\n" +
                    "Убедитесь, что класс public и реализует NavisHelper.Interfaces.IDevScript.");

            LastLoadedPath = dllPath;
            LastLoadedTime = DateTime.Now;
            LastLoadedVersion = assembly.GetName().Version.ToString();
            LastFileTime = File.GetLastWriteTime(dllPath);
            LoadedScripts = scripts;

            return scripts;
        }

        /// <summary>
        /// Перезагружает последнюю DLL.
        /// </summary>
        public static List<IDevScript> Reload()
        {
            if (string.IsNullOrEmpty(LastLoadedPath))
                throw new InvalidOperationException("Нет загруженной DLL для перезагрузки.");

            return LoadScripts(LastLoadedPath);
        }
    }
}
