using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;

namespace ColorService
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🎨 ColorService - Локальный сервис для работы с ИИ (OpenRouter)");

            if (args.Length < 2)
            {
                Console.WriteLine("❌ Использование: ColorService.exe <request_file> <response_file> [model] [thinking]");
                Console.WriteLine("   model    — короткое имя модели (например, claude-sonnet-4.6)");
                Console.WriteLine("   thinking — true/false (режим расширенного мышления)");
                return;
            }

            var requestFile = args[0];
            var responseFile = args[1];
            var modelName = args.Length > 2 ? args[2] : "claude-sonnet-4.6";
            var enableThinking = args.Length > 3 && args[3].Equals("true", StringComparison.OrdinalIgnoreCase);

            try
            {
                Console.WriteLine($"📁 Читаем запрос из: {requestFile}");

                if (!File.Exists(requestFile))
                {
                    Console.WriteLine("❌ Файл запроса не найден");
                    return;
                }

                var requestJson = File.ReadAllText(requestFile);
                var request = ParseColorRequest(requestJson);

                Console.WriteLine($"📋 Получено объектов: {request.objects?.Count ?? 0}");
                Console.WriteLine($"🤖 Модель: {modelName}, Thinking: {enableThinking}");

                if (request.objects == null || request.objects.Count == 0)
                {
                    WriteEmptyResponse(responseFile);
                    return;
                }

                var schemeName = request.schemeName ?? "Архитектурная";
                var (colors, rawResponse) = await GetColorsFromAI(request.objects, schemeName, modelName, enableThinking);

                WriteResponse(responseFile, request.requestId, colors, rawResponse);

                Console.WriteLine($"✅ Обработано цветов: {colors.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                WriteErrorResponse(responseFile, ex.Message);
            }
        }

        // Маппинг коротких имён → полные ID моделей OpenRouter
        static readonly Dictionary<string, string> ModelMap = new Dictionary<string, string>
        {
            { "claude-sonnet-4.6",  "anthropic/claude-sonnet-4.6" },
            { "claude-opus-4.6",    "anthropic/claude-opus-4.6" },
            { "glm-5-turbo",        "z-ai/glm-5-turbo" },
            { "gpt-5.4",            "openai/gpt-5.4" },
            { "gemini-3-flash",     "google/gemini-3-flash-preview" },
        };

        // Модели с поддержкой thinking
        static readonly HashSet<string> ThinkingModels = new HashSet<string>
        {
            "claude-sonnet-4.6", "claude-opus-4.6", "gemini-3-flash"
        };

        static string GetFullModelId(string shortName)
        {
            return ModelMap.ContainsKey(shortName) ? ModelMap[shortName] : ModelMap.Values.First();
        }

        static async Task<(Dictionary<string, string> colors, string rawResponse)> GetColorsFromAI(
            List<string> objectNames, string schemeName, string modelName, bool enableThinking)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPEN_ROUTER_NW_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("⚠️ OPEN_ROUTER_NW_KEY не найден, используем локальные цвета");
                return (GetLocalColors(objectNames), null);
            }

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(60);
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    client.DefaultRequestHeaders.Add("User-Agent", "NavisHelper-ColorService/2.0");

                    var fullModelId = GetFullModelId(modelName);
                    var useThinking = enableThinking && ThinkingModels.Contains(modelName);

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

                    var escapedPrompt = EscapeJsonString(prompt);

                    string requestBody;
                    if (useThinking)
                    {
                        requestBody = $"{{\"model\":\"{fullModelId}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{escapedPrompt}\"}}],\"max_tokens\":14000,\"thinking\":{{\"type\":\"enabled\",\"budget_tokens\":10240}}}}";
                        Console.WriteLine($"📡 Отправляем запрос к OpenRouter (thinking ON)...");
                    }
                    else
                    {
                        requestBody = $"{{\"model\":\"{fullModelId}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{escapedPrompt}\"}}],\"temperature\":0.3,\"max_tokens\":2000}}";
                        Console.WriteLine($"📡 Отправляем запрос к OpenRouter...");
                    }

                    var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

                    Console.WriteLine($"🤖 Модель: {fullModelId}");
                    var response = await client.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"❌ API ошибка: {response.StatusCode} - {errorContent}");
                        return (GetLocalColors(objectNames), $"API Error: {response.StatusCode}\n{errorContent}");
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"📥 Получен ответ: {responseContent.Length} символов");

                    var colors = ParseAIResponse(responseContent);

                    if (colors.Count > 0)
                    {
                        return (colors, responseContent);
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Цвета не найдены в ответе, используем локальные");
                        return (GetLocalColors(objectNames), responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка API: {ex.Message}");
                return (GetLocalColors(objectNames), $"Exception: {ex.Message}");
            }
        }

        static Dictionary<string, string> ParseAIResponse(string responseContent)
        {
            var colors = new Dictionary<string, string>();

            // Ищем content в ответе (может быть обычный текст или thinking response)
            // Паттерн 1: стандартный "content":"..."
            var contentMatch = Regex.Match(responseContent, @"""content"":\s*""((?:[^""\\]|\\.)*)""");
            string extractedContent = null;

            if (contentMatch.Success)
            {
                extractedContent = contentMatch.Groups[1].Value
                    .Replace("\\n", "\n")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
            }

            // Паттерн 2: content может быть объектом (thinking models)
            if (extractedContent == null)
            {
                // Попробуем найти JSON с colors прямо в ответе
                extractedContent = responseContent;
            }

            // Извлекаем пары object/color
            var colorMatches = Regex.Matches(extractedContent, @"""object"":\s*""([^""]+)"",\s*""color"":\s*""(\d+,\d+,\d+)""");

            foreach (Match match in colorMatches)
            {
                var objectName = match.Groups[1].Value;
                var color = match.Groups[2].Value;
                colors[objectName] = color;
                Console.WriteLine($"🎨 Найден цвет: {objectName} → {color}");
            }

            // Альтернативный парсинг (экранированные кавычки)
            if (colors.Count == 0)
            {
                var escapedMatches = Regex.Matches(responseContent, @"\\""object\\"":\s*\\""([^\\""]+)\\"",\s*\\""color\\"":\s*\\""(\d+,\d+,\d+)\\""");
                foreach (Match match in escapedMatches)
                {
                    var objectName = match.Groups[1].Value;
                    var color = match.Groups[2].Value;
                    colors[objectName] = color;
                    Console.WriteLine($"🎨 Найден цвет (escaped): {objectName} → {color}");
                }
            }

            return colors;
        }

        static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(value.Length + 32);
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

        /// <summary>
        /// Простой fallback без AI: каждому объекту — свой цвет из фиксированной палитры.
        /// Группировка не делается — это задача AI-модели.
        /// </summary>
        static Dictionary<string, string> GetLocalColors(List<string> objectNames)
        {
            // Нейтральная палитра для fallback
            var palette = new[]
            {
                "200,195,185", "185,190,200", "195,185,175", "175,195,190",
                "210,200,180", "180,185,195", "190,200,190", "200,185,195",
                "185,195,180", "195,190,200", "180,200,195", "200,190,185",
                "190,180,200", "195,200,185", "185,200,190", "200,185,180",
            };

            var result = new Dictionary<string, string>();
            for (int i = 0; i < objectNames.Count; i++)
            {
                var color = palette[i % palette.Length];
                result[objectNames[i]] = color;
                Console.WriteLine($"🎨 Fallback цвет: {objectNames[i]} → {color}");
            }

            return result;
        }

        static ColorRequest ParseColorRequest(string json)
        {
            try
            {
                var request = new ColorRequest();
                request.objects = new List<string>();

                var objectMatches = Regex.Matches(json, @"""objects"":\s*\[(.*?)\]", RegexOptions.Singleline);
                if (objectMatches.Count > 0)
                {
                    var objectsStr = objectMatches[0].Groups[1].Value;
                    var objectMatches2 = Regex.Matches(objectsStr, @"""([^""]+)""");
                    foreach (Match match in objectMatches2)
                    {
                        request.objects.Add(match.Groups[1].Value);
                    }
                }

                // Читаем schemeName
                var schemeMatch = Regex.Match(json, @"""schemeName"":\s*""([^""]+)""");
                if (schemeMatch.Success)
                    request.schemeName = schemeMatch.Groups[1].Value;

                // Читаем requestId
                var idMatch = Regex.Match(json, @"""requestId"":\s*""([^""]+)""");
                if (idMatch.Success)
                    request.requestId = idMatch.Groups[1].Value;

                return request;
            }
            catch
            {
                return new ColorRequest { objects = new List<string>() };
            }
        }

        static void WriteResponse(string responseFile, string requestId, Dictionary<string, string> colors, string rawResponse)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var colorsJson = string.Join(",", colors.Select(c =>
                $"{{\"object_name\":\"{EscapeJsonString(c.Key)}\",\"color\":\"{c.Value}\"}}"));
            var rawEscaped = rawResponse != null ? $",\"rawResponse\":\"{EscapeJsonString(rawResponse)}\"" : "";

            var json = $"{{\"timestamp\":\"{timestamp}\",\"requestId\":\"{requestId ?? Guid.NewGuid().ToString()}\",\"colors\":[{colorsJson}]{rawEscaped}}}";

            File.WriteAllText(responseFile, json);
            Console.WriteLine($"📄 Ответ записан в: {responseFile}");
        }

        static void WriteEmptyResponse(string responseFile)
        {
            var json = $"{{\"timestamp\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\"requestId\":\"{Guid.NewGuid()}\",\"colors\":[]}}";
            File.WriteAllText(responseFile, json);
        }

        static void WriteErrorResponse(string responseFile, string error)
        {
            var json = $"{{\"timestamp\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\"requestId\":\"{Guid.NewGuid()}\",\"colors\":[],\"error\":\"{EscapeJsonString(error)}\"}}";
            File.WriteAllText(responseFile, json);
        }
    }

    public class ColorRequest
    {
        public string timestamp { get; set; }
        public List<string> objects { get; set; }
        public string requestId { get; set; }
        public string schemeName { get; set; }
    }

    public class ColorResponse
    {
        public string timestamp { get; set; }
        public string requestId { get; set; }
        public List<ColorItem> colors { get; set; }
        public string error { get; set; }
    }

    public class ColorItem
    {
        public string object_name { get; set; }
        public string color { get; set; }
    }
}
