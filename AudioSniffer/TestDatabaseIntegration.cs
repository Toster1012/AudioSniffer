using AudioSniffer.Data;
using AudioSniffer.Models;
using AudioSniffer.Services;
using Microsoft.EntityFrameworkCore;

namespace AudioSniffer
{
    public class TestDatabaseIntegration
    {
        public static async Task TestAsync()
        {
            // Настройка сервисов
            IServiceCollection service_collection = new ServiceCollection();

            // Настройка контекста базы данных
            service_collection.AddDbContext<ApplicationDbContext>(database_options =>
                database_options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=AudioSnifferRequestHistory;Trusted_Connection=True;MultipleActiveResultSets=true"));

            // Регистрация сервиса истории запросов
            service_collection.AddScoped<IRequestHistoryService, RequestHistoryService>();

            ServiceProvider service_provider = service_collection.BuildServiceProvider();

            try
            {
                // Получение сервиса истории запросов
                IRequestHistoryService history_service = service_provider.GetRequiredService<IRequestHistoryService>();

                Console.WriteLine("Тестирование сервиса истории запросов...");

                // Добавление тестовых записей
                await history_service.AddRequestHistoryAsync("test1.wav", true);
                await history_service.AddRequestHistoryAsync("test2.mp3", false);
                await history_service.AddRequestHistoryAsync("test3.aac", true);

                Console.WriteLine("Добавлено 3 записи в историю запросов.");

                // Получение истории запросов
                List<RequestHistory> history_list = await history_service.GetRequestHistoryAsync();

                Console.WriteLine($"Получено {history_list.Count} записей из истории:");
                foreach (RequestHistory history_item in history_list)
                {
                    Console.WriteLine($"- {history_item.FileName} (Сгенерирован: {history_item.IsGenerated}, Дата: {history_item.RequestDate})");
                }

                Console.WriteLine("Тестирование успешно завершено!");
            }
            catch (Exception caught_exception)
            {
                Console.WriteLine($"Ошибка при тестировании: {caught_exception.Message}");
            }
        }
    }
}