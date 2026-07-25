using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("AIColorObjects", "CBC", DisplayName = "AI Color Objects")]
    [AddInPlugin(AddInLocation.None)]
    public class AIColorObjects : AddInPlugin
    {
                 private readonly LocalColorBridge _colorService;

        public AIColorObjects()
        {
            Logger.Info("🔧 Инициализация AIColorObjects...", "AIColorObjects");
            try
            {
                                                  _colorService = new LocalColorBridge(new NavisWorksLogger());
                 Logger.Info("✅ LocalColorBridge успешно создан (через файлы)", "AIColorObjects");
            }
            catch (Exception ex)
            {
                Logger.Error($"❌ Ошибка создания AIColorService: {ex.GetType().Name}: {ex.Message}", "AIColorObjects");
                Logger.Error($"❌ Stack trace: {ex.StackTrace}", "AIColorObjects");
                throw; // Перебрасываем исключение дальше
            }
        }

        public override int Execute(params string[] parameters)
        {
            Logger.Info("🚀 === НАЧАЛО Execute AIColorObjects ===", "AIColorObjects");
            Logger.Info($"📋 Параметры: {(parameters?.Length > 0 ? string.Join(", ", parameters) : "нет")}", "AIColorObjects");
            
            try
            {
                // Проверка доступности Application
                if (!ValidateApplication())
                    return 0;

                // Получение активного документа
                Document doc = GetActiveDocument();
                if (doc == null)
                    return 0;

                // Получение текущего выделения
                Selection currentSelection = GetCurrentSelection(doc);
                if (currentSelection == null)
                    return 0;

                // Получение выбранных элементов
                ModelItemCollection selection = GetSelectedItems(currentSelection);
                if (selection == null || selection.Count == 0)
                {
                    Logger.Info("Нет выделенных элементов", "AIColorObjects");
                    try
                    {
                        var panel = WPF.NavisHelperPanel.Current;
                        if (panel != null)
                            panel.Dispatcher.Invoke(() => panel.UpdateAIResponseLog(null));
                    }
                    catch { }
                    return 0;
                }

                // Фильтрация объектов, которые поддерживают изменение цвета
                var colorableSelection = AIColorUtils.FilterColorableObjects(selection);
                if (colorableSelection.Count == 0)
                {
                    Logger.Info("Нет объектов, поддерживающих изменение цвета", "AIColorObjects");
                    return 0;
                }

                // Извлечение имен объектов
                var objectNames = AIColorUtils.GetObjectNamesFromSelection(colorableSelection);
                if (objectNames.Count == 0)
                {
                    Logger.Info("Не удалось извлечь имена объектов", "AIColorObjects");
                    return 0;
                }

                Logger.Info($"Найдено объектов для окрашивания: {objectNames.Count}", "AIColorObjects");

                                 // Получение цветов от локального сервиса
                 Logger.Info("🌉 Начинаем получение цветов через мост-сервис...", "AIColorObjects");
                Logger.Info($"📝 Список объектов: {string.Join(", ", objectNames)}", "AIColorObjects");
                
                                 // Получение информации о локальном сервисе
                 Logger.Info("🔍 Получение информации о локальном сервисе...", "AIColorObjects");
                 try
                 {
                     var serviceInfo = _colorService.GetServiceInfo();
                     Logger.Info($"ℹ️ Информация о сервисе: {serviceInfo}", "AIColorObjects");
                     Logger.Info("✅ Локальный сервис готов к работе (без интернета)", "AIColorObjects");
                 }
                 catch (Exception ex)
                 {
                     Logger.Error($"❌ Ошибка получения информации о сервисе: {ex.Message}", "AIColorObjects");
                     return 0;
                 }
                
                Dictionary<string, string> colors = null;
                try
                {
                    Logger.Info("📡 Создание задачи для получения цветов...", "AIColorObjects");
                    
                    // Запускаем задачу и ждем результат
                                         Logger.Info("🚀 Запуск GetColorsForObjects (локальная версия)...", "AIColorObjects");
                     
                     // Используем локальный сервис без интернета
                     colors = _colorService.GetColorsForObjects(objectNames);
                    
                    Logger.Info($"📊 Результат получен: {colors?.Count ?? 0} цветов", "AIColorObjects");
                    
                    Logger.Info($"📊 Задача завершена, получено цветов: {colors?.Count ?? 0}", "AIColorObjects");
                    
                    if (colors != null)
                    {
                        Logger.Info($"🔍 Проверка результата: colors != null = {colors != null}", "AIColorObjects");
                        Logger.Info($"🔍 Количество цветов: {colors.Count}", "AIColorObjects");
                        
                        if (colors.Count > 0)
                        {
                            Logger.Info("🎨 Цвета успешно получены:", "AIColorObjects");
                            foreach (var kvp in colors)
                            {
                                Logger.Info($"  📋 {kvp.Key} → {kvp.Value}", "AIColorObjects");
                            }
                        }
                        else
                        {
                            Logger.Error("❌ Пустой результат от ИИ-сервиса", "AIColorObjects");
                            Logger.Info("🔍 Попытка получить информацию о сервисе...", "AIColorObjects");
                            try
                            {
                                                                 var serviceInfo = _colorService.GetServiceInfo();
                                Logger.Info($"ℹ️ Информация о сервисе: {serviceInfo}", "AIColorObjects");
                            }
                            catch (Exception infoEx)
                            {
                                Logger.Error($"❌ Не удалось получить информацию о сервисе: {infoEx.Message}", "AIColorObjects");
                            }
                            return 0;
                        }
                    }
                    else
                    {
                        Logger.Error("❌ Результат null от ИИ-сервиса", "AIColorObjects");
                        return 0;
                    }
                }
                catch (AggregateException aggEx)
                {
                    Logger.Error($"❌ AggregateException при получении цветов: {aggEx.Message}", "AIColorObjects");
                    if (aggEx.InnerExceptions.Count > 0)
                    {
                        foreach (var innerEx in aggEx.InnerExceptions)
                        {
                            Logger.Error($"  🔍 Внутреннее исключение: {innerEx.GetType().Name}: {innerEx.Message}", "AIColorObjects");
                        }
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Error($"❌ Ошибка при получении цветов: {ex.GetType().Name}: {ex.Message}", "AIColorObjects");
                    Logger.Error($"❌ Stack trace: {ex.StackTrace}", "AIColorObjects");
                    return 0;
                }

                if (colors == null || colors.Count == 0)
                {
                    Logger.Error("Не удалось получить цвета от ИИ-сервиса", "AIColorObjects");
                    return 0;
                }
                Logger.Info("✅ Цвета от ИИ-сервиса получены", "AIColorObjects");

                // Постобработка: если модель не сгруппировала (слишком много уникальных цветов),
                // группируем локально по буквенному корню имени
                var uniqueColors = new HashSet<string>(colors.Values).Count;
                if (uniqueColors > 10 && uniqueColors > colors.Count / 2)
                {
                    Logger.Info($"⚠️ Модель вернула {uniqueColors} уникальных цветов — постобработка группировки", "AIColorObjects");
                    colors = PostProcessGroupColors(colors);
                    Logger.Info($"✅ После группировки: {new HashSet<string>(colors.Values).Count} уникальных цветов", "AIColorObjects");
                }

                // Применение цветов к объектам
                var appliedCount = AIColorUtils.ApplyColorsToObjects(colorableSelection, colors);

                Logger.Info($"Применено цветов: {appliedCount}", "AIColorObjects");

                // Создание отчета
                var report = AIColorUtils.CreateColorReport(colors);
                Logger.Info($"Отчет: {report}", "AIColorObjects");

                // Обновить лог на UI-панели + сохранить в историю
                try
                {
                    var panel = WPF.NavisHelperPanel.Current;
                    if (panel != null)
                        panel.Dispatcher.Invoke(() =>
                        {
                            panel.UpdateAIResponseLog(colors);
                            panel.AddColorHistory(objectNames, colors, colorableSelection);
                        });
                }
                catch (Exception uiEx)
                {
                    Logger.Error($"Не удалось обновить UI лог: {uiEx.Message}", "AIColorObjects");
                }
                
                Logger.Info("✅ === УСПЕШНОЕ ЗАВЕРШЕНИЕ Execute AIColorObjects ===", "AIColorObjects");
                return appliedCount > 0 ? 1 : 0;
            }
            catch (COMException comEx)
            {
                Logger.Error($"🔒 COM ошибка: {comEx.Message}", "AIColorObjects");
                Logger.Error($"🔒 COM Error Code: {comEx.ErrorCode}, HResult: {comEx.HResult}", "AIColorObjects");
                Logger.Error("❌ === ЗАВЕРШЕНИЕ Execute AIColorObjects С COM ОШИБКОЙ ===", "AIColorObjects");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"💥 Неожиданная ошибка: {ex.Message}", "AIColorObjects");
                Logger.Error($"💥 Тип ошибки: {ex.GetType().Name}", "AIColorObjects");
                Logger.Error($"💥 Stack trace: {ex.StackTrace}", "AIColorObjects");
                Logger.Error("❌ === ЗАВЕРШЕНИЕ Execute AIColorObjects С ОШИБКОЙ ===", "AIColorObjects");
                return 0;
            }
        }

        private bool ValidateApplication()
        {
            try
            {
                var current = Application.MainDocument;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Application недоступен: {ex.Message}", "AIColorObjects");
                return false;
            }
        }

        private Document GetActiveDocument()
        {
            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null)
                {
                    Logger.Error("Нет активного документа", "AIColorObjects");
                    return null;
                }
                return doc;
            }
            catch (COMException comEx)
            {
                Logger.Error($"COM ошибка при получении активного документа: {comEx.Message}", "AIColorObjects");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при получении активного документа: {ex.Message}", "AIColorObjects");
                return null;
            }
        }

        private Selection GetCurrentSelection(Document doc)
        {
            try
            {
                var selection = doc.CurrentSelection;
                if (selection == null)
                {
                    Logger.Error("Не удалось получить текущее выделение", "AIColorObjects");
                    return null;
                }
                return selection;
            }
            catch (COMException comEx)
            {
                Logger.Error($"COM ошибка при получении текущего выделения: {comEx.Message}", "AIColorObjects");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при получении текущего выделения: {ex.Message}", "AIColorObjects");
                return null;
            }
        }

        private ModelItemCollection GetSelectedItems(Selection currentSelection)
        {
            try
            {
                var selection = currentSelection.GetSelectedItems();
                if (selection == null)
                {
                    Logger.Error("Не удалось получить выбранные элементы", "AIColorObjects");
                    return null;
                }
                return selection;
            }
            catch (COMException comEx)
            {
                Logger.Error($"COM ошибка при получении выбранных элементов: {comEx.Message}", "AIColorObjects");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при получении выбранных элементов: {ex.Message}", "AIColorObjects");
                return null;
            }
        }

        /// <summary>
        /// Постобработка: если AI не сгруппировал, группируем по буквенному корню имени
        /// </summary>
        private Dictionary<string, string> PostProcessGroupColors(Dictionary<string, string> colors)
        {
            // Извлекаем буквенный корень из имени объекта
            // "/24-008-ОВ_ВЕ1.3" → "ВЕ"
            // "/240100-ТК1-Футляр1220_1" → "Футляр"
            // "/2419-АС_Ограждения_кровля" → "Ограждения"
            var groups = new Dictionary<string, List<string>>();

            foreach (var kvp in colors)
            {
                var root = ExtractTypeRoot(kvp.Key);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<string>();
                groups[root].Add(kvp.Key);
            }

            // Получаем палитру из текущей схемы
            var scheme = AIConfig.Instance.GetColorSchemeType();
            var palette = ColorSchemes.GetPalette(scheme);
            if (palette == null || palette.Length == 0)
                palette = new[] { "180,100,100", "100,180,100", "100,100,180", "180,180,100", "180,100,180", "100,180,180", "200,150,100", "150,100,200" };

            var result = new Dictionary<string, string>();
            int colorIdx = 0;

            foreach (var group in groups)
            {
                var groupColor = palette[colorIdx % palette.Length];
                foreach (var name in group.Value)
                    result[name] = groupColor;
                colorIdx++;
            }

            Logger.Info($"📊 Постобработка: {colors.Count} объектов → {groups.Count} групп", "AIColorObjects");
            return result;
        }

        /// <summary>
        /// Извлекает буквенный корень типа объекта из полного имени.
        /// Ищет первый значимый буквенный сегмент среди частей после разделителей.
        /// Пропускает чисто числовые части (номера, индексы, коды).
        /// </summary>
        private static string ExtractTypeRoot(string fullName)
        {
            var name = fullName.TrimStart('/');
            // Разбиваем по всем разделителям: -, _, пробел
            var parts = name.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Ищем первый сегмент с буквенным корнем, пропуская чисто числовые
            // и общие префиксы-идентификаторы (обычно первый сегмент).
            // Для "/Резервуар_1.14_Корпус" → ["Резервуар","1.14","Корпус"]
            //   "Резервуар" — общий префикс, "1.14" — число, "Корпус" — тип
            // Для "/24-008-ОВ_ВЕ1.3" → ["24","008","ОВ","ВЕ1.3"]
            //   "24","008" — числа, "ОВ" — префикс раздела, "ВЕ1.3" → корень "ВЕ"

            // Стратегия: идём с конца, ищем первый сегмент, дающий буквенный корень длиной >= 2.
            // Если не нашли — идём с начала и берём любой буквенный корень.
            for (int i = parts.Length - 1; i >= 1; i--)
            {
                var root = ExtractLetterPrefix(parts[i]);
                if (root.Length >= 2)
                    return root;
            }

            // Fallback: берём буквенный корень из последней части после первого разделителя,
            // или из первой части если разделителей нет
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var root = ExtractLetterPrefix(parts[i]);
                if (root.Length > 0)
                    return root;
            }

            return parts.Length > 0 ? parts[parts.Length - 1] : name;
        }

        /// <summary>
        /// Извлекает буквенный префикс строки (до первого не-буквенного символа)
        /// </summary>
        private static string ExtractLetterPrefix(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in s)
            {
                if (char.IsLetter(ch))
                    sb.Append(ch);
                else
                    break;
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            _colorService?.Dispose();
        }
    }

     // Локальный мост-сервис для связи с ИИ через файлы
     public class LocalColorBridge : IDisposable
     {
         private readonly ILogger _logger;
         private readonly string _requestFile;
         private readonly string _responseFile;
                   private string _serviceExePath;
         private readonly AIColorService _aiService;

         public LocalColorBridge(ILogger logger = null)
         {
             _logger = logger ?? new ConsoleLogger();
             var guid = Guid.NewGuid().ToString("N");
             _requestFile = Path.Combine(Path.GetTempPath(), $"navis_color_request_{guid}.json");
             _responseFile = Path.Combine(Path.GetTempPath(), $"navis_color_response_{guid}.json");
                           _serviceExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ColorService.exe");
             _aiService = new AIColorService();
             
             _logger.Info("🔧 Инициализация LocalColorBridge...", "LocalColorBridge");
             _logger.Info($"📁 Файл запроса: {_requestFile}", "LocalColorBridge");
             _logger.Info($"📁 Файл ответа: {_responseFile}", "LocalColorBridge");
         }

         public Dictionary<string, string> GetColorsForObjects(List<string> objectNames)
         {
             _logger.Info($"=== НАЧАЛО GetColorsForObjects для {objectNames?.Count ?? 0} объектов ===", "LocalColorBridge");
             
             try
             {
                 if (objectNames == null || objectNames.Count == 0)
                 {
                     _logger.Error("❌ Список объектов пуст", "LocalColorBridge");
                     return new Dictionary<string, string>();
                 }

                 // Очищаем старые файлы
                 CleanupFiles();
                 
                                   // Записываем запрос в файл
                  WriteRequestToFile(objectNames);
                  _logger.Info("📝 Запрос записан в файл", "LocalColorBridge");
                  
                  // Проверяем, что файл создался
                  if (File.Exists(_requestFile))
                  {
                      var content = File.ReadAllText(_requestFile);
                      _logger.Info($"📄 Содержимое файла запроса: {content}", "LocalColorBridge");
                  }
                  else
                  {
                      _logger.Error("❌ Файл запроса не создался!", "LocalColorBridge");
                  }
                 
                 // Запускаем локальный сервис
                 if (StartLocalService())
                 {
                     // Ждем ответа
                     var colors = WaitForResponse();
                     _logger.Info($"✅ Получено цветов: {colors?.Count ?? 0}", "LocalColorBridge");
                     return colors ?? new Dictionary<string, string>();
                 }
                 else
                 {
                     // Fallback: используем локальные цвета
                     _logger.Info("🔄 Fallback: используем локальные цвета", "LocalColorBridge");
                     return GetLocalColors(objectNames);
                 }
             }
             catch (Exception ex)
             {
                 _logger.Error($"❌ Ошибка получения цветов: {ex.Message}", "LocalColorBridge");
                 return new Dictionary<string, string>();
             }
             finally
             {
                 CleanupFiles();
             }
         }

         private void WriteRequestToFile(List<string> objectNames)
         {
             try
             {
                 var schemeName = ColorSchemes.GetSchemeNameRu(AIConfig.Instance.GetColorSchemeType());
                 var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                 var requestId = Guid.NewGuid().ToString();

                 var json = $"{{\"timestamp\":\"{EscapeJsonString(timestamp)}\",\"objects\":[{string.Join(",", objectNames.Select(o => $"\"{EscapeJsonString(o)}\""))}],\"requestId\":\"{EscapeJsonString(requestId)}\",\"schemeName\":\"{EscapeJsonString(schemeName)}\"}}";

                 File.WriteAllText(_requestFile, json);
                 _logger.Info($"📄 Запрос записан: {json.Length} символов", "LocalColorBridge");
             }
             catch (Exception ex)
             {
                 _logger.Error($"❌ Ошибка записи запроса: {ex.Message}", "LocalColorBridge");
                 throw;
             }
         }

         private bool StartLocalService()
         {
             try
             {
                 // Проверяем, есть ли исполняемый файл сервиса
                                   if (!File.Exists(_serviceExePath))
                  {
                      _logger.Info("⚠️ Файл ColorService.exe не найден, используем fallback", "LocalColorBridge");
                      _logger.Info($"🔍 Ищем в: {_serviceExePath}", "LocalColorBridge");
                      
                      // Попробуем найти в других местах
                      var alternativePaths = new[]
                      {
                          Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ColorService.exe"),
                          Path.Combine(Directory.GetCurrentDirectory(), "ColorService.exe"),
                          Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "ColorService.exe")
                      };
                      
                      foreach (var path in alternativePaths)
                      {
                          if (File.Exists(path))
                          {
                              _logger.Info($"✅ Найден в: {path}", "LocalColorBridge");
                              _serviceExePath = path;
                              break;
                          }
                      }
                      
                      if (!File.Exists(_serviceExePath))
                      {
                          _logger.Error("❌ ColorService.exe не найден нигде", "LocalColorBridge");
                          return false;
                      }
                  }

                                   // Запускаем сервис в фоне
                  var modelName = AIConfig.Instance.ModelName;
                  var thinking = AIConfig.Instance.EnableThinking ? "true" : "false";
                  var startInfo = new System.Diagnostics.ProcessStartInfo
                  {
                      FileName = _serviceExePath,
                      Arguments = $"\"{_requestFile}\" \"{_responseFile}\" \"{modelName}\" {thinking}",
                      UseShellExecute = false,
                      CreateNoWindow = true,
                      RedirectStandardOutput = true,
                      RedirectStandardError = true
                  };

                                   using (var process = System.Diagnostics.Process.Start(startInfo))
                  {
                      _logger.Info($"🚀 Локальный сервис запущен (PID: {process?.Id})", "LocalColorBridge");
                      
                      // Добавляем отладку
                      if (process != null)
                      {
                          _logger.Info($"📊 Process ID: {process.Id}", "LocalColorBridge");
                          _logger.Info($"📊 Process Name: {process.ProcessName}", "LocalColorBridge");
                          _logger.Info($"📊 Process Start Time: {process.StartTime}", "LocalColorBridge");
                          
                          // Ждем немного и проверяем статус
                          Thread.Sleep(1000);
                          
                          if (process.HasExited)
                          {
                              _logger.Error($"❌ Процесс завершился с кодом: {process.ExitCode}", "LocalColorBridge");
                              
                              // Читаем вывод процесса
                              try
                              {
                                  var output = process.StandardOutput.ReadToEnd();
                                  var error = process.StandardError.ReadToEnd();
                                  
                                  if (!string.IsNullOrEmpty(output))
                                      _logger.Info($"📤 Вывод процесса: {output}", "LocalColorBridge");
                                  if (!string.IsNullOrEmpty(error))
                                      _logger.Error($"❌ Ошибка процесса: {error}", "LocalColorBridge");
                              }
                              catch (Exception ex)
                              {
                                  _logger.Error($"❌ Ошибка чтения вывода: {ex.Message}", "LocalColorBridge");
                              }
                              
                              return false;
                          }
                          else
                          {
                              _logger.Info("✅ Процесс работает", "LocalColorBridge");
                          }
                      }
                      
                      return process != null;
                  }
             }
             catch (Exception ex)
             {
                 _logger.Error($"❌ Ошибка запуска сервиса: {ex.Message}", "LocalColorBridge");
                 return false;
             }
         }

         private Dictionary<string, string> WaitForResponse()
         {
             var timeout = TimeSpan.FromSeconds(30);
             var startTime = DateTime.Now;
             
             _logger.Info("⏳ Ожидание ответа от локального сервиса...", "LocalColorBridge");
             
             while (DateTime.Now - startTime < timeout)
             {
                 if (File.Exists(_responseFile))
                 {
                     try
                     {
                         var json = File.ReadAllText(_responseFile);
                         var response = ParseColorResponse(json);
                         
                         if (response?.colors != null)
                         {
                             var result = new Dictionary<string, string>();
                             foreach (var color in response.colors)
                             {
                                 if (!string.IsNullOrEmpty(color.object_name) && !string.IsNullOrEmpty(color.color))
                                 {
                                     result[color.object_name] = color.color;
                                 }
                             }
                             
                             _logger.Info($"✅ Ответ получен: {result.Count} цветов", "LocalColorBridge");
                             return result;
                         }
                     }
                     catch (Exception ex)
                     {
                         _logger.Error($"❌ Ошибка чтения ответа: {ex.Message}", "LocalColorBridge");
                     }
                 }
                 
                 Thread.Sleep(100); // Ждем 100мс
             }
             
             _logger.Error("⏰ Таймаут ожидания ответа", "LocalColorBridge");
             return null;
         }

         private void CleanupFiles()
         {
             try
             {
                 if (File.Exists(_requestFile))
                     File.Delete(_requestFile);
                 if (File.Exists(_responseFile))
                     File.Delete(_responseFile);
             }
             catch (Exception ex)
             {
                 _logger.Error($"❌ Ошибка очистки файлов: {ex.Message}", "LocalColorBridge");
             }
         }

         public string GetServiceInfo()
         {
             var info = $"Local Color Bridge - File-based communication with AI service";
             _logger.Info($"ℹ️ Информация о сервисе: {info}", "LocalColorBridge");
             return info;
         }

         public void Dispose()
         {
             CleanupFiles();
             _aiService?.Dispose();
             _logger.Info("🗑️ Освобождение ресурсов LocalColorBridge...", "LocalColorBridge");
         }

         // Классы для сериализации
         private class ColorResponse
         {
             public string timestamp { get; set; }
             public string requestId { get; set; }
             public List<ColorItem> colors { get; set; }
         }

         private class ColorItem
         {
             public string object_name { get; set; }
             public string color { get; set; }
         }

         private ColorResponse ParseColorResponse(string json)
         {
             try
             {
                 var response = new ColorResponse();
                 response.colors = new List<ColorItem>();
                 
                 // Простой парсинг JSON для цветов
                 var colorMatches = System.Text.RegularExpressions.Regex.Matches(json, @"""object_name"":\s*""([^""]+)"",\s*""color"":\s*""([^""]+)""");
                 
                 foreach (System.Text.RegularExpressions.Match match in colorMatches)
                 {
                     response.colors.Add(new ColorItem
                     {
                         object_name = match.Groups[1].Value,
                         color = match.Groups[2].Value
                     });
                 }
                 
                 return response;
             }
             catch
             {
                 return new ColorResponse { colors = new List<ColorItem>() };
             }
         }

        private Dictionary<string, string> GetLocalColors(List<string> objectNames)
        {
            // Получаем текущую цветовую схему из конфига
            var schemeType = AIConfig.Instance.GetColorSchemeType();
            _logger.Info($"🎨 Используем цветовую схему: {(int)schemeType} - {ColorSchemes.GetSchemeNameRu(schemeType)}", "LocalColorBridge");
            
            // Генерируем цвета через ColorSchemes
            var colors = ColorSchemes.GenerateColorsForObjects(schemeType, objectNames);
            
            foreach (var kvp in colors)
            {
                _logger.Info($"🎨 Цвет для '{kvp.Key}': {kvp.Value}", "LocalColorBridge");
            }

            return colors;
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new System.Text.StringBuilder(value.Length + 16);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(ch))
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
     }

     // Локальный сервис цветов без интернета
     public class LocalColorService : IDisposable
     {
         private readonly ILogger _logger;
         private readonly Dictionary<string, string> _colorMap;
         private static readonly Random _random = new Random();

         public LocalColorService(ILogger logger = null)
         {
             _logger = logger ?? new ConsoleLogger();
             _colorMap = InitializeColorMap();
             
             _logger.Info("🔧 Инициализация LocalColorService (без интернета)...", "LocalColorService");
             _logger.Info($"🎨 Загружено {_colorMap.Count} предустановленных цветов", "LocalColorService");
         }

         public Dictionary<string, string> GetColorsForObjects(List<string> objectNames)
         {
             _logger.Info($"=== НАЧАЛО GetColorsForObjects для {objectNames?.Count ?? 0} объектов ===", "LocalColorService");
             
             try
             {
                 if (objectNames == null || objectNames.Count == 0)
                 {
                     _logger.Error("❌ Список объектов пуст", "LocalColorService");
                     return new Dictionary<string, string>();
                 }

                 var result = new Dictionary<string, string>();
                 
                 foreach (var objectName in objectNames)
                 {
                     var color = GetColorForObject(objectName);
                     if (!string.IsNullOrEmpty(color))
                     {
                         result[objectName] = color;
                         _logger.Info($"🎨 Найден цвет для '{objectName}': {color}", "LocalColorService");
                     }
                     else
                     {
                         var randomColor = GenerateRandomColor();
                         result[objectName] = randomColor;
                         _logger.Info($"🎲 Случайный цвет для '{objectName}': {randomColor}", "LocalColorService");
                     }
                 }

                 _logger.Info($"✅ Успешно получено цветов: {result.Count}", "LocalColorService");
                 return result;
             }
             catch (Exception ex)
             {
                 _logger.Error($"❌ Ошибка получения цветов: {ex.Message}", "LocalColorService");
                 return new Dictionary<string, string>();
             }
         }

         private string GetColorForObject(string objectName)
         {
             if (string.IsNullOrWhiteSpace(objectName))
                 return null;

             var lowerName = objectName.ToLowerInvariant();
             foreach (var kvp in _colorMap)
             {
                 if (lowerName.Contains(kvp.Key.ToLowerInvariant()))
                 {
                     return kvp.Value;
                 }
             }
             return null;
         }

         private string GenerateRandomColor()
         {
             lock (_random)
             {
                 var r = _random.Next(50, 200);
                 var g = _random.Next(50, 200);
                 var b = _random.Next(50, 200);
                 return $"{r},{g},{b}";
             }
         }

         private Dictionary<string, string> InitializeColorMap()
         {
             return new Dictionary<string, string>
             {
                 {"стена", "245,222,179"}, {"wall", "245,222,179"},
                 {"труба", "30,144,255"}, {"pipe", "30,144,255"},
                 {"кабель", "255,255,0"}, {"cable", "255,255,0"},
                 {"резервуар", "0,128,128"}, {"tank", "0,128,128"},
                 {"металл", "192,192,192"}, {"metal", "192,192,192"},
                 {"насос", "255,140,0"}, {"pump", "255,140,0"},
                 {"дверь", "160,82,45"}, {"door", "160,82,45"},
                 {"окно", "135,206,235"}, {"window", "135,206,235"}
             };
         }

         public string GetServiceInfo()
         {
             var info = $"Local Color Service - {_colorMap.Count} predefined colors, No Internet Required";
             _logger.Info($"ℹ️ Информация о сервисе: {info}", "LocalColorService");
             return info;
         }

         public void Dispose()
         {
             _logger.Info("🗑️ Освобождение ресурсов LocalColorService...", "LocalColorService");
         }
     }

     // Логгер для NavisWorks
     public class NavisWorksLogger : ILogger
     {
         public void Info(string message, string context = null)
         {
             Logger.Info($"[AIColorService] {message}", context ?? "AIColorService");
         }

         public void Error(string message, string context = null)
         {
             Logger.Error($"[AIColorService] {message}", context ?? "AIColorService");
         }
     }
 }
