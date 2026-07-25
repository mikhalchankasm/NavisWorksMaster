namespace NavisHelper.Interfaces
{
    /// <summary>
    /// Интерфейс для тестовых скриптов, загружаемых через DevScriptLoader.
    /// Тестовая DLL должна содержать класс, реализующий этот интерфейс.
    /// </summary>
    public interface IDevScript
    {
        /// <summary>Название скрипта для отображения в UI.</summary>
        string Name { get; }

        /// <summary>Основная логика скрипта.</summary>
        void Run();
    }
}
