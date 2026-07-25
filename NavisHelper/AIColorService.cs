using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using NavisHelper.Core;
using System.Linq;
using System.Runtime.InteropServices;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    /// <summary>
    /// Сервис для получения цветов объектов через ИИ-API
    /// </summary>
    public class AIColorService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly AIConfig _config;
        private readonly ILogger _logger;

        public AIColorService(AIConfig config = null, ILogger logger = null)
        {
            _config = config ?? AIConfig.Instance;
            _logger = logger ?? new ConsoleLogger();
            
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMilliseconds(_config.RequestTimeout);
            
            // Добавить дополнительные заголовки для избежания блокировок
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NavisHelper/1.0");
            _httpClient.DefaultRequestHeaders.ConnectionClose = true;
            
            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
            }
            
            // Теперь можно безопасно использовать _logger
            _logger.Info("🔧 Инициализация AIColorService...", "AIColorService");
            _logger.Info($"⚙️ Конфигурация: {(_config == null ? "null" : "загружена")}", "AIColorService");
            _logger.Info($"📝 Логгер: {(_logger.GetType().Name)}", "AIColorService");
            _logger.Info($"⏱️ HTTP таймаут установлен: {_config.RequestTimeout}ms", "AIColorService");
            _logger.Info("🔗 HTTP заголовки установлены: User-Agent, Connection: close", "AIColorService");
            
            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                _logger.Info("🔑 API ключ установлен в заголовках", "AIColorService");
            }
            else
            {
                _logger.Error("❌ API ключ не найден. Установите переменную окружения OPEN_ROUTER_NW_KEY", "AIColorService");
            }
            
            _logger.Info("✅ AIColorService инициализирован успешно", "AIColorService");
        }

        /// <summary>
        /// Получает цвета для списка объектов через ИИ-сервис (синхронная версия)
        /// </summary>
        /// <param name="objectNames">Список имен объектов</param>
        /// <returns>Словарь имя объекта -> RGB цвет</returns>
        public Dictionary<string, string> GetColorsForObjects(List<string> objectNames)
        {
            _logger.Info($"=== НАЧАЛО GetColorsForObjects для {objectNames?.Count ?? 0} объектов ===", "AIColorService");
            
            try
            {
                // Проверка состояния NavisWorks перед запросом
                if (!IsNavisWorksReady())
                {
                    _logger.Error("❌ NavisWorks не готов к работе - документ заблокирован или недоступен", "AIColorService");
                    return new Dictionary<string, string>();
                }

                if (!_config.IsValid())
                {
                    _logger.Error("❌ Конфигурация ИИ-сервиса невалидна", "AIColorService");
                    return new Dictionary<string, string>();
                }

                if (objectNames == null || objectNames.Count == 0)
                {
                    _logger.Error("❌ Список объектов пуст", "AIColorService");
                    return new Dictionary<string, string>();
                }

                _logger.Info($"✅ Проверки пройдены. Начинаем запросы к ИИ-сервису...", "AIColorService");

                for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
                {
                    _logger.Info($"🔄 Попытка {attempt}/{_config.MaxRetries} - создание промпта...", "AIColorService");
                    
                    try
                    {
                        var prompt = CreatePrompt(objectNames);
                        _logger.Info($"📝 Промпт создан ({prompt.Length} символов)", "AIColorService");
                        
                        var request = CreateRequest(prompt);
                        _logger.Info($"📤 Отправка HTTP запроса к {_config.ApiUrl}...", "AIColorService");
                        
                        var response = _httpClient.PostAsync(_config.ApiUrl, request).Result;
                        _logger.Info($"📥 Получен ответ: {response.StatusCode}", "AIColorService");
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorContent = response.Content.ReadAsStringAsync().Result;
                            _logger.Error($"❌ Ошибка API: {response.StatusCode} - {errorContent}", "AIColorService");
                            
                            if (attempt < _config.MaxRetries)
                            {
                                var delay = 1000 * attempt;
                                _logger.Info($"⏳ Ожидание {delay}ms перед повторной попыткой...", "AIColorService");
                                Task.Delay(delay).Wait();
                                continue;
                            }
                            return new Dictionary<string, string>();
                        }

                        _logger.Info($"📖 Чтение содержимого ответа...", "AIColorService");
                        var content = response.Content.ReadAsStringAsync().Result;
                        _logger.Info($"📄 Ответ получен ({content.Length} символов)", "AIColorService");
                        _logger.Info($"📄 Содержимое ответа: {content}", "AIColorService");
                        
                        _logger.Info($"🔍 Парсинг ответа...", "AIColorService");
                        var result = ParseResponse(content, objectNames);
                        
                        if (result.Count > 0)
                        {
                            _logger.Info($"✅ Успешно получено цветов: {result.Count}", "AIColorService");
                            foreach (var kvp in result)
                            {
                                _logger.Info($"  🎨 {kvp.Key} → {kvp.Value}", "AIColorService");
                            }
                            return result;
                        }
                        else if (attempt < _config.MaxRetries)
                        {
                            _logger.Info($"⚠️ Пустой ответ, повторная попытка {attempt + 1}", "AIColorService");
                            var delay = 1000 * attempt;
                            _logger.Info($"⏳ Ожидание {delay}ms...", "AIColorService");
                            Task.Delay(delay).Wait();
                            continue;
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.Error($"❌ HTTP ошибка (попытка {attempt}): {ex.Message}", "AIColorService");
                        if (attempt < _config.MaxRetries)
                        {
                            var delay = 1000 * attempt;
                            _logger.Info($"⏳ Ожидание {delay}ms перед повторной попыткой...", "AIColorService");
                            Task.Delay(delay).Wait();
                            continue;
                        }
                    }
                    catch (TaskCanceledException ex)
                    {
                        _logger.Error($"⏰ Таймаут запроса (попытка {attempt}): {ex.Message}", "AIColorService");
                        if (attempt < _config.MaxRetries)
                        {
                            var delay = 1000 * attempt;
                            _logger.Info($"⏳ Ожидание {delay}ms перед повторной попыткой...", "AIColorService");
                            Task.Delay(delay).Wait();
                            continue;
                        }
                    }
                    catch (COMException comEx)
                    {
                        _logger.Error($"🔒 COM ошибка - NavisWorks заблокирован (попытка {attempt}): {comEx.Message}", "AIColorService");
                        _logger.Error($"🔒 COM Error Code: {comEx.ErrorCode}, HResult: {comEx.HResult}", "AIColorService");
                        if (attempt < _config.MaxRetries)
                        {
                            var delay = 1000 * attempt;
                            _logger.Info($"⏳ Ожидание {delay}ms перед повторной попыткой...", "AIColorService");
                            Task.Delay(delay).Wait();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"💥 Неожиданная ошибка (попытка {attempt}): {ex.Message}", "AIColorService");
                        _logger.Error($"💥 Тип ошибки: {ex.GetType().Name}", "AIColorService");
                        if (attempt < _config.MaxRetries)
                        {
                            var delay = 1000 * attempt;
                            _logger.Info($"⏳ Ожидание {delay}ms перед повторной попыткой...", "AIColorService");
                            Task.Delay(delay).Wait();
                            continue;
                        }
                    }
                }

                _logger.Error($"❌ Не удалось получить цвета после {_config.MaxRetries} попыток", "AIColorService");
                return new Dictionary<string, string>();
            }
            catch (COMException comEx)
            {
                _logger.Error($"🔒 Критическая COM ошибка: {comEx.Message}", "AIColorService");
                _logger.Error($"🔒 COM Error Code: {comEx.ErrorCode}, HResult: {comEx.HResult}", "AIColorService");
                return new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.Error($"💥 Критическая ошибка: {ex.Message}", "AIColorService");
                _logger.Error($"💥 Тип ошибки: {ex.GetType().Name}", "AIColorService");
                return new Dictionary<string, string>();
            }
            finally
            {
                _logger.Info($"=== КОНЕЦ GetColorsForObjects ===", "AIColorService");
            }
        }

        /// <summary>
        /// Получает цвета для списка объектов через ИИ-сервис (асинхронная версия для совместимости)
        /// </summary>
        /// <param name="objectNames">Список имен объектов</param>
        /// <returns>Словарь имя объекта -> RGB цвет</returns>
        public async Task<Dictionary<string, string>> GetColorsForObjectsAsync(List<string> objectNames)
        {
            return await Task.Run(() => GetColorsForObjects(objectNames));
        }

        /// <summary>
        /// Проверяет доступность сервиса
        /// </summary>
        /// <returns>true если сервис доступен</returns>
        public async Task<bool> IsServiceAvailableAsync()
        {
            _logger.Info("🔍 Проверка доступности ИИ-сервиса...", "AIColorService");
            
            try
            {
                _logger.Info("🔍 Проверка конфигурации...", "AIColorService");
                if (!_config.IsValid())
                {
                    _logger.Error("❌ Конфигурация невалидна для проверки доступности", "AIColorService");
                    _logger.Error($"❌ API Key: {(_config.ApiKey?.Length > 0 ? "установлен" : "отсутствует")}", "AIColorService");
                    _logger.Error($"❌ API URL: {_config.ApiUrl}", "AIColorService");
                    _logger.Error($"❌ Timeout: {_config.RequestTimeout}ms", "AIColorService");
                    return false;
                }
                
                _logger.Info("✅ Конфигурация валидна", "AIColorService");
                _logger.Info($"📡 Тестовый запрос к {_config.ApiUrl}...", "AIColorService");
                
                var testRequest = CreateRequest("test");
                _logger.Info("📤 Тестовый запрос создан, отправка...", "AIColorService");
                
                var response = await _httpClient.PostAsync(_config.ApiUrl, testRequest);
                _logger.Info($"📥 Получен ответ: {response.StatusCode}", "AIColorService");
                
                var isAvailable = response.IsSuccessStatusCode;
                _logger.Info($"📊 Результат проверки: {(isAvailable ? "✅ Доступен" : "❌ Недоступен")} ({response.StatusCode})", "AIColorService");
                
                if (!isAvailable)
                {
                    try
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.Error($"❌ Детали ошибки: {errorContent}", "AIColorService");
                    }
                    catch (Exception readEx)
                    {
                        _logger.Error($"❌ Не удалось прочитать детали ошибки: {readEx.Message}", "AIColorService");
                    }
                }
                
                return isAvailable;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"❌ HTTP ошибка при проверке доступности: {ex.Message}", "AIColorService");
                _logger.Error($"❌ Тип ошибки: {ex.GetType().Name}", "AIColorService");
                if (ex.InnerException != null)
                {
                    _logger.Error($"🔍 Внутреннее исключение: {ex.InnerException.Message}", "AIColorService");
                }
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger.Error($"⏰ Таймаут при проверке доступности: {ex.Message}", "AIColorService");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"💥 Неожиданная ошибка при проверке доступности: {ex.Message}", "AIColorService");
                _logger.Error($"💥 Тип ошибки: {ex.GetType().Name}", "AIColorService");
                if (ex.InnerException != null)
                {
                    _logger.Error($"🔍 Внутреннее исключение: {ex.InnerException.Message}", "AIColorService");
                }
                return false;
            }
        }

        /// <summary>
        /// Получает информацию о сервисе
        /// </summary>
        /// <returns>Информация о сервисе</returns>
        public string GetServiceInfo()
        {
            var info = $"OpenRouter AI Service - URL: {_config.ApiUrl}, Model: {AIModels.GetFullId(_config.ModelName)}, Thinking: {_config.EnableThinking}, Timeout: {_config.RequestTimeout}ms";
            _logger.Info($"ℹ️ Информация о сервисе: {info}", "AIColorService");
            return info;
        }

        /// <summary>
        /// Проверяет состояние NavisWorks
        /// </summary>
        private bool IsNavisWorksReady()
        {
            try
            {
                _logger.Info("🔍 Проверка состояния NavisWorks...", "AIColorService");
                
                var doc = Application.ActiveDocument;
                if (doc == null)
                {
                    _logger.Error("❌ Активный документ не найден", "AIColorService");
                    return false;
                }
                
                _logger.Info("✅ Документ найден, проверка выделения...", "AIColorService");
                
                // Проверяем, что документ не заблокирован
                var test = doc.CurrentSelection;
                if (test == null)
                {
                    _logger.Error("❌ Не удается получить текущее выделение", "AIColorService");
                    return false;
                }
                
                _logger.Info("✅ NavisWorks готов к работе", "AIColorService");
                return true;
            }
            catch (COMException comEx)
            {
                _logger.Error($"🔒 COM ошибка при проверке NavisWorks: {comEx.Message}", "AIColorService");
                _logger.Error($"🔒 COM Error Code: {comEx.ErrorCode}, HResult: {comEx.HResult}", "AIColorService");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"💥 Ошибка при проверке NavisWorks: {ex.Message}", "AIColorService");
                return false;
            }
        }

        private string CreatePrompt(List<string> objectNames)
        {
            _logger.Info($"📝 Создание промпта для {objectNames.Count} объектов...", "AIColorService");

            var schemeName = ColorSchemes.GetSchemeNameRu(_config.GetColorSchemeType());
            var namesList = string.Join("\n", objectNames);
            var prompt = $@"Ты — эксперт по цветовому оформлению 3D BIM-моделей (Navisworks).

ЗАДАЧА: подобрать RGB-цвета для списка объектов строительной модели.

ВЫБРАННАЯ ЦВЕТОВАЯ СХЕМА: «{schemeName}»
Придерживайся характера и палитры этой схемы при выборе оттенков.

СПИСОК ОБЪЕКТОВ (каждый на отдельной строке):
{namesList}

КРИТИЧЕСКОЕ ПРАВИЛО — УКРУПНЁННАЯ ГРУППИРОВКА:
Ты ОБЯЗАН группировать объекты КРУПНО. Итоговых цветовых групп должно быть МАЛО (обычно 3–8).
ЗАПРЕЩЕНО назначать каждому объекту свой цвет. Если у тебя получилось больше 10 групп — ты группируешь слишком мелко, укрупни.

КАК ОПРЕДЕЛИТЬ ГРУППУ:
1. Найди ОБЩИЙ ПРЕФИКС всех объектов (например, «Резервуар_1.14_» или «ОВ_»). Отбрось его — он не несёт информации о типе.
2. В ОСТАВШЕЙСЯ части имени найди БУКВЕННЫЙ КОРЕНЬ — первое слово или 1–3 буквы, определяющие ТИП/КОМПОНЕНТ объекта. Числа, индексы, номера — НЕ ВАЖНЫ, отбрось их.
3. Все объекты с одним буквенным корнем = ОДНА группа = ОДИН цвет.

ВАЖНО: если ВСЕ объекты начинаются одинаково (например, «Резервуар_1.14_...»), ты ОБЯЗАН группировать по РАЗЛИЧАЮЩЕЙСЯ части после общего префикса, а НЕ по самому префиксу!

ПРИМЕРЫ (разные проекты):

Компоненты оборудования:
  Резервуар_1.14_Корпус, Резервуар_1.14_Крыша → общий префикс «Резервуар_1.14_», корни «Корпус» и «Крыша» — ДВЕ разные группы.
  Резервуар_1.14_Штуцера, Резервуар_1.14_Штуцера2 → корень «Штуцера» = ОДНА группа.
  Резервуар_1.14_ЛОГО_1, Резервуар_1.14_ЛОГО_2 → корень «ЛОГО» = ОДНА группа.
  Резервуар_1.14_ПРИМЕРКОМПАНИЯ, Резервуар_1.14_ПРИМЕРКОМПАНИЯ_ФОН_1021, Резервуар_1.14_ПРИМЕРКОМПАНИЯ_ФОН_2011 → корень «ПРИМЕРКОМПАНИЯ» = ОДНА группа.
  ЗАПРЕЩЕНО красить все части резервуара одним цветом! Группируй по компоненту.

Трубопроводы (нефтегаз):
  Н3.1.1, Н3.9.2, Н13.2, Н45, Н52.1, Н70.3, Н73.2 → ВСЕ начинаются на «Н» = одна группа «трубопроводы», ОДИН цвет.
  Ш1, Ш1-1, Ш2, Ш2-1 → ВСЕ начинаются на «Ш» = одна группа «шахты», ОДИН цвет.
  Футляр1220_1, Футляр1920_3, Футляр426_1, Футляр720_2 → ВСЕ начинаются на «Футляр» = ОДИН цвет.
  Д1, Д2, Д3, Д1/1 → ВСЕ начинаются на «Д» = ОДИН цвет.

Вентиляция (ОВ):
  ОВ_В1, ОВ_В2 → общий префикс «ОВ_», корень «В» (вытяжка) = ОДИН цвет.
  ОВ_ВА1, ОВ_ВА2.1, ОВ_ВА2.2, ОВ_ВА3.1, ОВ_ВА3.2 → корень «ВА» (вентиляция аварийная) = ОДИН цвет.
  ОВ_ВЕ1.1, ОВ_ВЕ1.2, ОВ_ВЕ1.3, ОВ_ВЕ1.4, ОВ_ВЕ2, ОВ_ВЕ3 → корень «ВЕ» (вент. естественная) = ОДИН цвет.
  ОВ_П1, ОВ_П2, ОВ_П3 → корень «П» (приток) = ОДИН цвет.
  Итого для 29 объектов вентиляции: ВСЕГО 6 групп.

Архитектура (АС):
  АС_Окна → одна группа. АС_Двери → другая группа. АС_Ограждения, АС_Ограждения_кровля → одна группа.

ПРОВЕРЬ СЕБЯ:
- Если число групп = 1 и объектов больше 3 — ты сгруппировал слишком крупно, разбей по компонентам.
- Если число групп > 10 или равно числу объектов — ты группируешь слишком мелко, укрупни.

ДОПОЛНИТЕЛЬНЫЕ ПРАВИЛА:
1. Используй реалистичные строительные/инженерные оттенки в рамках выбранной схемы.
2. Избегай чистого белого (255,255,255) и чистого чёрного (0,0,0).
3. Цвета разных групп должны быть ХОРОШО РАЗЛИЧИМЫ (разница минимум 40 единиц по одному из каналов RGB).

ФОРМАТ ОТВЕТА — строго JSON, без markdown-обёртки, без ```json, без пояснений:
{{
  ""colors"": [
    {{""object"": ""точное_имя_объекта_из_списка"", ""color"": ""R,G,B""}},
    ...
  ]
}}

Где R, G, B — целые числа от 0 до 255. Верни цвет для КАЖДОГО объекта из списка выше.";

            _logger.Info($"📄 Промпт создан ({prompt.Length} символов)", "AIColorService");
            return prompt;
        }

        private HttpContent CreateRequest(string prompt)
        {
            _logger.Info("📤 Создание HTTP запроса...", "AIColorService");

            var fullModelId = AIModels.GetFullId(_config.ModelName);
            var supportsThinking = _config.EnableThinking && AIModels.SupportsThinking(_config.ModelName);

            string json;
            if (supportsThinking)
            {
                // Для моделей с thinking: budget_tokens + отключаем temperature
                var budgetTokens = Math.Max(10240, _config.MaxTokens * 5);
                var totalTokens = budgetTokens + _config.MaxTokens;
                json = $@"{{""model"":""{EscapeJsonString(fullModelId)}"",""messages"":[{{""role"":""user"",""content"":""{EscapeJsonString(prompt)}""}}],""max_tokens"":{totalTokens},""thinking"":{{""type"":""enabled"",""budget_tokens"":{budgetTokens}}}}}";
                _logger.Info($"📋 Thinking mode ON: модель={fullModelId}, budget_tokens={budgetTokens}, max_tokens={totalTokens}", "AIColorService");
            }
            else
            {
                json = $@"{{""model"":""{EscapeJsonString(fullModelId)}"",""messages"":[{{""role"":""user"",""content"":""{EscapeJsonString(prompt)}""}}],""temperature"":{_config.Temperature.ToString(CultureInfo.InvariantCulture)},""max_tokens"":{_config.MaxTokens}}}";
                _logger.Info($"📋 HTTP запрос: модель={fullModelId}, температура={_config.Temperature}, токены={_config.MaxTokens}", "AIColorService");
            }

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return content;
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 16);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
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

        private Dictionary<string, string> ParseResponse(string responseContent, List<string> objectNames)
        {
            _logger.Info($"🔍 Начало парсинга ответа ({responseContent.Length} символов)...", "AIColorService");
            
            try
            {
                var result = new Dictionary<string, string>();
                
                // Извлекаем content из ответа - надежный способ через IndexOf
                var startIndex = responseContent.IndexOf("\"content\":\"");
                _logger.Info($"🔍 Поиск content: startIndex = {startIndex}", "AIColorService");
                
                if (startIndex != -1)
                {
                    startIndex += "\"content\":\"".Length;
                    _logger.Info($"🔍 Начальная позиция content: {startIndex}", "AIColorService");
                    
                    // Ищем конец content - ищем закрывающую кавычку после content
                    var endIndex = -1;
                    var searchStart = startIndex;
                    
                    // Ищем закрывающую кавычку, которая не является частью экранированной последовательности
                    while (true)
                    {
                        endIndex = responseContent.IndexOf("\"", searchStart);
                        if (endIndex == -1) break;
                        
                        // Проверяем, что это не экранированная кавычка
                        if (endIndex > 0 && responseContent[endIndex - 1] != '\\')
                        {
                            break;
                        }
                        searchStart = endIndex + 1;
                    }
                    
                    _logger.Info($"🔍 Конечная позиция content: {endIndex}", "AIColorService");
                    
                    if (endIndex != -1)
                    {
                        var content = responseContent.Substring(startIndex, endIndex - startIndex);
                        _logger.Info($"🔍 Извлеченный content: {content}", "AIColorService");
                        
                        // Парсим JSON из content - исправленный Regex для экранированных кавычек
                        _logger.Info($"🔍 Проверка наличия colors в content: {content.Contains("\\\"colors\\\"")}", "AIColorService");
                        
                        if (content.Contains("\\\"colors\\\""))
                        {
                            _logger.Info("🎯 Найдено поле colors, начинаем парсинг...", "AIColorService");
                            var colorMatches = Regex.Matches(content, @"\\\""object\\\"":\s*\\\""([^\\\""]+)\\\"",\s*\\\""color\\\"":\s*\\\""([^\\\""]+)\\\""");
                            _logger.Info($"🎯 Найдено совпадений Regex: {colorMatches.Count}", "AIColorService");
                            
                            foreach (Match match in colorMatches)
                            {
                                var objectName = match.Groups[1].Value;
                                var color = match.Groups[2].Value;
                                
                                _logger.Info($"🎯 Найдены совпадения: объект='{objectName}', цвет='{color}'", "AIColorService");
                                
                                if (IsValidRgbColor(color))
                                {
                                    result[objectName] = color;
                                    _logger.Info($"🎨 Найден цвет: {objectName} → {color}", "AIColorService");
                                }
                                else
                                {
                                    _logger.Error($"⚠️ Некорректный цвет для {objectName}: {color}", "AIColorService");
                                }
                            }
                        }
                        else
                        {
                            _logger.Info("⚠️ Поле colors не найдено в content", "AIColorService");
                            _logger.Info($"🔍 Содержимое content для анализа: {content}", "AIColorService");
                        }
                    }
                    else
                    {
                        _logger.Info("⚠️ Не найден конец content", "AIColorService");
                    }
                }
                else
                {
                    _logger.Info("⚠️ Не найден content в ответе", "AIColorService");
                    _logger.Info($"🔍 Поиск в строке: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}...", "AIColorService");
                }
                
                // Если JSON не удалось распарсить, пытаемся извлечь цвета из текста
                if (result.Count == 0)
                {
                    _logger.Info("⚠️ JSON парсинг не удался, пробуем извлечь цвета из текста...", "AIColorService");
                    result = ExtractColorsFromText(responseContent, objectNames);
                }

                _logger.Info($"📊 Результат парсинга: {result.Count} цветов извлечено", "AIColorService");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Ошибка парсинга ответа: {ex.Message}", "AIColorService");
                _logger.Error($"❌ Тип ошибки: {ex.GetType().Name}", "AIColorService");
                return ExtractColorsFromText(responseContent, objectNames);
            }
        }

        private Dictionary<string, string> ExtractColorsFromText(string text, List<string> objectNames)
        {
            _logger.Info("🔍 Извлечение цветов из текста...", "AIColorService");
            
            var result = new Dictionary<string, string>();
            var lines = text.Split('\n');
            _logger.Info($"📄 Анализ {lines.Length} строк текста для {objectNames.Count} объектов", "AIColorService");
            
            foreach (var line in lines)
            {
                foreach (var objectName in objectNames)
                {
                    if (line.Contains(objectName) && line.Contains(","))
                    {
                        _logger.Info($"🔍 Найдена строка с объектом '{objectName}': {line.Trim()}", "AIColorService");
                        
                        var parts = line.Split(',');
                        if (parts.Length >= 3)
                        {
                            var colorPart = string.Join(",", parts.Skip(Math.Max(0, parts.Length - 3)));
                            _logger.Info($"🎨 Извлеченная часть цвета: '{colorPart}'", "AIColorService");
                            
                            if (IsValidRgbColor(colorPart))
                            {
                                result[objectName] = colorPart;
                                _logger.Info($"🎨 Извлечен цвет из текста: {objectName} → {colorPart}", "AIColorService");
                                break;
                            }
                            else
                            {
                                _logger.Error($"⚠️ Некорректный цвет '{colorPart}' для объекта '{objectName}'", "AIColorService");
                            }
                        }
                        else
                        {
                            _logger.Info($"⚠️ Недостаточно частей в строке для объекта '{objectName}': {parts.Length} частей", "AIColorService");
                        }
                    }
                }
            }

            _logger.Info($"📊 Извлечено цветов из текста: {result.Count}", "AIColorService");
            return result;
        }

        private bool IsValidRgbColor(string colorString)
        {
            _logger.Info($"🔍 Валидация цвета: '{colorString}'", "AIColorService");
            
            try
            {
                var parts = colorString.Split(',');
                if (parts.Length != 3)
                {
                    _logger.Error($"⚠️ Неверное количество частей цвета: {parts.Length} (ожидается 3)", "AIColorService");
                    return false;
                }
                
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i].Trim();
                    if (!int.TryParse(part, out int value))
                    {
                        _logger.Error($"⚠️ Не удалось распарсить часть {i}: '{part}'", "AIColorService");
                        return false;
                    }
                    
                    if (value < 0 || value > 255)
                    {
                        _logger.Error($"⚠️ Значение цвета вне диапазона [0,255]: {value} в части {i}", "AIColorService");
                        return false;
                    }
                }
                
                _logger.Info($"✅ Цвет '{colorString}' валиден", "AIColorService");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Ошибка валидации цвета '{colorString}': {ex.Message}", "AIColorService");
                return false;
            }
        }

        public void Dispose()
        {
            _logger.Info("🗑️ Освобождение ресурсов AIColorService...", "AIColorService");
            _httpClient?.Dispose();
            _logger.Info("✅ Ресурсы освобождены", "AIColorService");
        }
    }

    // Модели данных для API
    public class AIResponse
    {
        public List<ColorInfo> Colors { get; set; }
    }

    public class ColorInfo
    {
        public string Object { get; set; }
        public string Color { get; set; }
    }

    // Простой логгер для случаев, когда основной недоступен
    public class ConsoleLogger : ILogger
    {
        public void Info(string message, string context = null)
        {
            Console.WriteLine($"[INFO] [{context}] {message}");
        }

        public void Error(string message, string context = null)
        {
            Console.WriteLine($"[ERROR] [{context}] {message}");
        }
    }

    public interface ILogger
    {
        void Info(string message, string context = null);
        void Error(string message, string context = null);
    }
}
