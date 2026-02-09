using AudioSniffer.Data;
using AudioSniffer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace TestRequestHistoryConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Тестирование базы данных истории запросов...");

            // Настройка сервисов
            var services = new ServiceCollection();

            // Настройка контекста базы данных
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=AudioSnifferRequestHistory;Trusted_Connection=True;MultipleActiveResultSets=true"));

            // Регистрация сервиса истории запросов
            services.AddScoped<IRequestHistoryService, RequestHistoryService>();

            var serviceProvider = services.BuildServiceProvider();

            try
            {
                // Получение сервиса истории запросов
                var historyService = serviceProvider.GetRequiredService<IRequestHistoryService>();

                // Добавление тестовых записей
                await historyService.AddRequestHistoryAsync("test1.wav", true);
                await historyService.AddRequestHistoryAsync("test2.mp3", false);
                await historyService.AddRequestHistoryAsync("test3.aac", true);

                Console.WriteLine("Добавлено 3 записи в историю запросов.");

                // Получение истории запросов
                var history = await historyService.GetRequestHistoryAsync();

                Console.WriteLine($"Получено {history.Count} записей из истории:");
                foreach (var item in history)
                {
                    Console.WriteLine($"- {item.FileName} (Сгенерирован: {item.IsGenerated}, Дата: {item.RequestDate})");
                }

                Console.WriteLine("Тестирование успешно завершено!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при тестировании: {ex.Message}");
                Console.WriteLine($"Стек вызовов: {ex.StackTrace}");
            }
        }
    }
}