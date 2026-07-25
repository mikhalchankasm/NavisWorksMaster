using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper
{
    /// <summary>
    /// Типы цветовых схем для автоматического окрашивания
    /// </summary>
    public enum ColorSchemeType
    {
        /// <summary>1 - Оттенки серого</summary>
        Grayscale = 1,
        
        /// <summary>2 - Серый + пастельные тона</summary>
        GrayPastel = 2,
        
        /// <summary>3 - Теплые тона (оранжевый, красный, желтый)</summary>
        Warm = 3,
        
        /// <summary>4 - Холодные тона (синий, голубой, бирюзовый)</summary>
        Cool = 4,
        
        /// <summary>5 - Земляные тона (коричневый, бежевый, терракотовый)</summary>
        Earth = 5,
        
        /// <summary>6 - Морская тема (синий, бирюзовый, аквамарин)</summary>
        Ocean = 6,
        
        /// <summary>7 - Лесная тема (зеленый, коричневый, оливковый)</summary>
        Forest = 7,
        
        /// <summary>8 - Архитектурная (нейтральные профессиональные тона)</summary>
        Architectural = 8,
        
        /// <summary>9 - Контрастная (яркие различимые цвета)</summary>
        HighContrast = 9,

        /// <summary>10 - Полностью случайные цвета</summary>
        Random = 10,

        /// <summary>11 - Промышленная (заводы, трубопроводы, оборудование)</summary>
        Industrial = 11,

        /// <summary>12 - Инфраструктурная (дороги, мосты, коммуникации)</summary>
        Infrastructure = 12,

        /// <summary>13 - Нефтегаз (резервуары, трубы, эстакады)</summary>
        OilGas = 13,

        /// <summary>14 - Пастельный радужный (мягкие различимые цвета)</summary>
        PastelRainbow = 14
    }

    /// <summary>
    /// Цветовые схемы для автоматического окрашивания объектов
    /// </summary>
    public static class ColorSchemes
    {
        private static readonly Random _random = new Random();

        /// <summary>
        /// Получить описание всех схем для отображения пользователю
        /// </summary>
        public static string GetSchemesDescription()
        {
            return @"Выберите цветовую схему (1-10):

1. Grayscale     - Оттенки серого
2. Gray+Pastel   - Серый + пастельные тона
3. Warm          - Теплые тона (оранжевый, красный, желтый)
4. Cool          - Холодные тона (синий, голубой, бирюзовый)
5. Earth         - Земляные тона (коричневый, бежевый)
6. Ocean         - Морская тема (синий, бирюзовый)
7. Forest        - Лесная тема (зеленый, коричневый)
8. Architectural - Архитектурная (нейтральные профессиональные)
9. High Contrast    - Контрастная (яркие различимые цвета)
10. Random          - Полностью случайные цвета
11. Industrial      - Промышленная (заводы, оборудование)
12. Infrastructure  - Инфраструктурная (дороги, мосты)
13. Oil & Gas       - Нефтегаз (резервуары, трубопроводы)
14. Pastel Rainbow  - Пастельная радуга (мягкие различимые)";
        }

        /// <summary>
        /// Получить палитру цветов для заданной схемы
        /// </summary>
        /// <param name="scheme">Тип схемы</param>
        /// <returns>Массив RGB цветов в формате "R,G,B"</returns>
        public static string[] GetPalette(ColorSchemeType scheme)
        {
            switch (scheme)
            {
                case ColorSchemeType.Grayscale: return GetGrayscalePalette();
                case ColorSchemeType.GrayPastel: return GetGrayPastelPalette();
                case ColorSchemeType.Warm: return GetWarmPalette();
                case ColorSchemeType.Cool: return GetCoolPalette();
                case ColorSchemeType.Earth: return GetEarthPalette();
                case ColorSchemeType.Ocean: return GetOceanPalette();
                case ColorSchemeType.Forest: return GetForestPalette();
                case ColorSchemeType.Architectural: return GetArchitecturalPalette();
                case ColorSchemeType.HighContrast: return GetHighContrastPalette();
                case ColorSchemeType.Random: return null; // Random генерируется на лету
                case ColorSchemeType.Industrial: return GetIndustrialPalette();
                case ColorSchemeType.Infrastructure: return GetInfrastructurePalette();
                case ColorSchemeType.OilGas: return GetOilGasPalette();
                case ColorSchemeType.PastelRainbow: return GetPastelRainbowPalette();
                default: return null;
            }
        }

        /// <summary>
        /// Генерирует цвет для объекта на основе схемы и имени
        /// </summary>
        /// <param name="scheme">Цветовая схема</param>
        /// <param name="objectName">Имя объекта (используется как seed для консистентности)</param>
        /// <param name="index">Индекс объекта в списке</param>
        /// <param name="totalCount">Общее количество уникальных объектов</param>
        /// <returns>RGB цвет в формате "R,G,B"</returns>
        public static string GenerateColor(ColorSchemeType scheme, string objectName, int index, int totalCount)
        {
            if (scheme == ColorSchemeType.Random)
            {
                return GenerateRandomColor();
            }

            var palette = GetPalette(scheme);
            if (palette == null || palette.Length == 0)
            {
                return GenerateRandomColor();
            }

            // Используем hash имени для консистентного выбора цвета
            int hash = GetStableHash(objectName);
            
            // Выбираем базовый цвет из палитры
            int paletteIndex = Math.Abs(hash) % palette.Length;
            string baseColor = palette[paletteIndex];
            
            // Добавляем небольшую вариацию для различимости
            return AddColorVariation(baseColor, index, totalCount);
        }

        /// <summary>
        /// Генерирует словарь цветов для списка имен объектов
        /// </summary>
        /// <param name="scheme">Цветовая схема</param>
        /// <param name="objectNames">Список уникальных имен объектов</param>
        /// <returns>Словарь имя -> RGB цвет</returns>
        public static Dictionary<string, string> GenerateColorsForObjects(ColorSchemeType scheme, List<string> objectNames)
        {
            var colors = new Dictionary<string, string>();
            
            if (objectNames == null || objectNames.Count == 0)
                return colors;

            var uniqueNames = objectNames.Distinct().ToList();
            
            for (int i = 0; i < uniqueNames.Count; i++)
            {
                string name = uniqueNames[i];
                string color = GenerateColor(scheme, name, i, uniqueNames.Count);
                colors[name] = color;
            }

            return colors;
        }

        #region Палитры

        private static string[] GetGrayscalePalette()
        {
            return new[]
            {
                "240,240,240", // Очень светло-серый
                "220,220,220", // Светло-серый
                "200,200,200", // Серебристый
                "180,180,180", // Серый
                "160,160,160", // Темно-серый
                "140,140,140", // Графит светлый
                "120,120,120", // Графит
                "100,100,100", // Темный графит
                "80,80,80",    // Угольный
                "60,60,60"     // Темный угольный
            };
        }

        private static string[] GetGrayPastelPalette()
        {
            return new[]
            {
                "200,200,200", // Серый
                "220,220,220", // Светло-серый
                "230,220,220", // Пастельный розовато-серый
                "220,230,220", // Пастельный зеленовато-серый
                "220,220,230", // Пастельный голубовато-серый
                "245,235,224", // Пастельный бежевый
                "224,235,245", // Пастельный голубой
                "235,245,224", // Пастельный мятный
                "245,224,235", // Пастельный лавандовый
                "240,235,220"  // Пастельный кремовый
            };
        }

        private static string[] GetWarmPalette()
        {
            return new[]
            {
                "255,200,150", // Персиковый
                "255,180,130", // Светлый оранжевый
                "255,160,100", // Оранжевый
                "240,140,90",  // Терракотовый светлый
                "230,180,150", // Бежево-оранжевый
                "255,220,180", // Кремово-желтый
                "250,200,160", // Абрикосовый
                "255,190,140", // Коралловый светлый
                "240,170,120", // Медовый
                "230,160,110"  // Янтарный
            };
        }

        private static string[] GetCoolPalette()
        {
            return new[]
            {
                "180,200,220", // Голубовато-серый
                "160,190,220", // Светло-голубой
                "140,180,210", // Голубой
                "150,180,200", // Стальной голубой
                "170,200,230", // Небесный
                "190,210,230", // Бледно-голубой
                "160,200,200", // Бирюзово-серый
                "150,190,210", // Аквамарин светлый
                "180,190,220", // Лавандово-голубой
                "170,195,215"  // Серо-голубой
            };
        }

        private static string[] GetEarthPalette()
        {
            return new[]
            {
                "210,180,140", // Загар
                "188,170,150", // Бежево-серый
                "200,160,120", // Карамельный
                "180,150,120", // Коричнево-бежевый
                "220,200,170", // Песочный
                "195,175,155", // Кофе с молоком
                "175,160,140", // Серо-коричневый
                "205,185,155", // Пшеничный
                "190,165,135", // Ореховый светлый
                "215,195,165"  // Слоновая кость теплая
            };
        }

        private static string[] GetOceanPalette()
        {
            return new[]
            {
                "150,200,210", // Аквамарин
                "130,180,200", // Морской светлый
                "160,190,200", // Бирюзово-серый
                "140,190,190", // Бирюзовый
                "170,210,220", // Светлая бирюза
                "150,180,190", // Серо-голубой морской
                "180,210,210", // Мятно-морской
                "160,195,200", // Морская пена
                "145,185,195", // Глубокий аквамарин
                "175,205,215"  // Небесно-морской
            };
        }

        private static string[] GetForestPalette()
        {
            return new[]
            {
                "160,180,140", // Шалфей
                "140,160,120", // Оливковый светлый
                "170,190,150", // Мятно-зеленый
                "150,170,130", // Зеленый мох
                "180,170,150", // Древесный
                "160,150,130", // Коричнево-серый
                "175,185,155", // Хаки светлый
                "165,175,145", // Зеленовато-серый
                "155,165,135", // Оливково-серый
                "185,180,160"  // Льняной
            };
        }

        private static string[] GetArchitecturalPalette()
        {
            return new[]
            {
                "240,240,235", // Белый теплый
                "230,225,220", // Слоновая кость
                "215,210,205", // Светлый камень
                "200,195,190", // Серый камень
                "185,180,175", // Бетон светлый
                "170,165,160", // Бетон
                "220,215,200", // Песчаник светлый
                "205,200,185", // Песчаник
                "225,220,210", // Мрамор светлый
                "210,205,195"  // Известняк
            };
        }

        private static string[] GetHighContrastPalette()
        {
            return new[]
            {
                "255,100,100", // Красный
                "100,255,100", // Зеленый
                "100,100,255", // Синий
                "255,255,100", // Желтый
                "255,100,255", // Маджента
                "100,255,255", // Циан
                "255,180,100", // Оранжевый
                "180,100,255", // Фиолетовый
                "100,255,180", // Морская волна
                "255,100,180"  // Розовый
            };
        }

        private static string[] GetIndustrialPalette()
        {
            return new[]
            {
                "180,180,170", // Сталь светлая
                "150,150,140", // Сталь
                "130,130,120", // Чугун
                "170,160,140", // Ржавчина светлая
                "200,190,170", // Алюминий
                "160,170,180", // Оцинковка
                "140,135,125", // Тёмная сталь
                "190,185,175", // Нержавейка
                "175,170,155", // Латунь тусклая
                "165,160,150"  // Промышленный серый
            };
        }

        private static string[] GetInfrastructurePalette()
        {
            return new[]
            {
                "180,180,180", // Бетон дорожный
                "160,155,145", // Асфальт светлый
                "140,150,160", // Металл мостовой
                "200,195,180", // Песок дорожный
                "170,180,170", // Зелёный обочины
                "190,185,175", // Бордюр
                "155,160,170", // Опора светлая
                "175,170,160", // Камень
                "185,190,185", // Ограждение дорожное
                "195,190,180"  // Плитка тротуарная
            };
        }

        private static string[] GetOilGasPalette()
        {
            return new[]
            {
                "120,130,140", // Труба стальная
                "100,110,120", // Труба тёмная
                "170,170,160", // Резервуар светлый
                "150,150,140", // Резервуар
                "180,175,160", // Эстакада
                "140,145,150", // Оборудование
                "160,155,145", // Задвижка
                "130,135,140", // Фланец
                "190,185,170", // Опора
                "110,120,130"  // Трубопровод тёмный
            };
        }

        private static string[] GetPastelRainbowPalette()
        {
            return new[]
            {
                "255,180,180", // Пастельный красный
                "255,210,170", // Пастельный оранжевый
                "255,240,170", // Пастельный жёлтый
                "200,240,180", // Пастельный зелёный
                "170,230,210", // Пастельный мятный
                "170,215,240", // Пастельный голубой
                "180,190,240", // Пастельный синий
                "210,185,240", // Пастельный фиолетовый
                "240,185,220", // Пастельный розовый
                "220,210,190"  // Пастельный бежевый
            };
        }

        #endregion

        #region Вспомогательные методы

        /// <summary>
        /// Генерирует случайный RGB цвет
        /// </summary>
        private static string GenerateRandomColor()
        {
            int r = _random.Next(50, 256);
            int g = _random.Next(50, 256);
            int b = _random.Next(50, 256);
            return $"{r},{g},{b}";
        }

        /// <summary>
        /// Стабильный хеш строки (не зависит от сессии)
        /// </summary>
        private static int GetStableHash(string str)
        {
            if (string.IsNullOrEmpty(str))
                return 0;

            unchecked
            {
                int hash = 17;
                foreach (char c in str)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }

        /// <summary>
        /// Добавляет небольшую вариацию к цвету для различимости
        /// </summary>
        private static string AddColorVariation(string baseColor, int index, int totalCount)
        {
            if (string.IsNullOrEmpty(baseColor))
                return baseColor;

            try
            {
                var parts = baseColor.Split(',');
                if (parts.Length != 3)
                    return baseColor;

                int r = int.Parse(parts[0]);
                int g = int.Parse(parts[1]);
                int b = int.Parse(parts[2]);

                // Вариация ±15 в зависимости от индекса
                int variation = (index * 7) % 31 - 15;
                
                r = Math.Max(0, Math.Min(255, r + variation));
                g = Math.Max(0, Math.Min(255, g + variation / 2));
                b = Math.Max(0, Math.Min(255, b - variation / 2));

                return $"{r},{g},{b}";
            }
            catch
            {
                return baseColor;
            }
        }

        /// <summary>
        /// Парсит номер схемы из строки ввода пользователя
        /// </summary>
        /// <param name="input">Строка от пользователя</param>
        /// <param name="scheme">Результат - тип схемы</param>
        /// <returns>true если успешно распарсено</returns>
        public static bool TryParseScheme(string input, out ColorSchemeType scheme)
        {
            scheme = ColorSchemeType.Random;
            
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (int.TryParse(input.Trim(), out int number))
            {
                if (number >= 1 && number <= 10)
                {
                    scheme = (ColorSchemeType)number;
                    return true;
                }
            }

            // Попробуем распарсить по имени
            input = input.Trim().ToLowerInvariant();
            
            var nameMapping = new Dictionary<string, ColorSchemeType>
            {
                { "grayscale", ColorSchemeType.Grayscale },
                { "gray", ColorSchemeType.Grayscale },
                { "grey", ColorSchemeType.Grayscale },
                { "серый", ColorSchemeType.Grayscale },
                
                { "graypastel", ColorSchemeType.GrayPastel },
                { "pastel", ColorSchemeType.GrayPastel },
                { "пастель", ColorSchemeType.GrayPastel },
                
                { "warm", ColorSchemeType.Warm },
                { "теплый", ColorSchemeType.Warm },
                { "теплая", ColorSchemeType.Warm },
                
                { "cool", ColorSchemeType.Cool },
                { "холодный", ColorSchemeType.Cool },
                { "холодная", ColorSchemeType.Cool },
                
                { "earth", ColorSchemeType.Earth },
                { "земля", ColorSchemeType.Earth },
                { "земляной", ColorSchemeType.Earth },
                
                { "ocean", ColorSchemeType.Ocean },
                { "море", ColorSchemeType.Ocean },
                { "морской", ColorSchemeType.Ocean },
                
                { "forest", ColorSchemeType.Forest },
                { "лес", ColorSchemeType.Forest },
                { "лесной", ColorSchemeType.Forest },
                
                { "architectural", ColorSchemeType.Architectural },
                { "arch", ColorSchemeType.Architectural },
                { "архитектура", ColorSchemeType.Architectural },
                { "архитектурный", ColorSchemeType.Architectural },
                
                { "highcontrast", ColorSchemeType.HighContrast },
                { "contrast", ColorSchemeType.HighContrast },
                { "контраст", ColorSchemeType.HighContrast },
                
                { "random", ColorSchemeType.Random },
                { "случайный", ColorSchemeType.Random },
                { "рандом", ColorSchemeType.Random },

                { "industrial", ColorSchemeType.Industrial },
                { "промышленная", ColorSchemeType.Industrial },
                { "промышленный", ColorSchemeType.Industrial },

                { "infrastructure", ColorSchemeType.Infrastructure },
                { "инфраструктура", ColorSchemeType.Infrastructure },
                { "инфраструктурная", ColorSchemeType.Infrastructure },

                { "oilgas", ColorSchemeType.OilGas },
                { "нефтегаз", ColorSchemeType.OilGas },
                { "нефть", ColorSchemeType.OilGas },

                { "pastelrainbow", ColorSchemeType.PastelRainbow },
                { "радуга", ColorSchemeType.PastelRainbow },
                { "пастельная", ColorSchemeType.PastelRainbow }
            };

            if (nameMapping.TryGetValue(input, out scheme))
                return true;

            return false;
        }

        /// <summary>
        /// Получить название схемы на русском
        /// </summary>
        public static string GetSchemeNameRu(ColorSchemeType scheme)
        {
            switch (scheme)
            {
                case ColorSchemeType.Grayscale: return "Оттенки серого";
                case ColorSchemeType.GrayPastel: return "Серый + пастельные";
                case ColorSchemeType.Warm: return "Теплые тона";
                case ColorSchemeType.Cool: return "Холодные тона";
                case ColorSchemeType.Earth: return "Земляные тона";
                case ColorSchemeType.Ocean: return "Морская тема";
                case ColorSchemeType.Forest: return "Лесная тема";
                case ColorSchemeType.Architectural: return "Архитектурная";
                case ColorSchemeType.HighContrast: return "Контрастная";
                case ColorSchemeType.Random: return "Случайные цвета";
                case ColorSchemeType.Industrial: return "Промышленная";
                case ColorSchemeType.Infrastructure: return "Инфраструктурная";
                case ColorSchemeType.OilGas: return "Нефтегаз";
                case ColorSchemeType.PastelRainbow: return "Пастельная радуга";
                default: return "Неизвестная схема";
            }
        }

        #endregion
    }
}

