using AudioSniffer.Data;
using AudioSniffer.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AudioSniffer.Services
{
    public interface IRequestHistoryService
    {
        Task AddRequestHistoryAsync(string fileName, bool isGenerated);
        Task<List<RequestHistory>> GetRequestHistoryAsync();
    }

    public class RequestHistoryService : IRequestHistoryService
    {
        private readonly ApplicationDbContext _database_context;

        public RequestHistoryService(ApplicationDbContext database_context)
        {
            _database_context = database_context;
        }

        public async Task AddRequestHistoryAsync(string file_name, bool is_generated)
        {
            RequestHistory history_entry = new RequestHistory
            {
                FileName = file_name,
                IsGenerated = is_generated,
                RequestDate = DateTime.UtcNow
            };

            _database_context.RequestHistories.Add(history_entry);
            await _database_context.SaveChangesAsync();
        }

        public async Task<List<RequestHistory>> GetRequestHistoryAsync()
        {
            return await _database_context.RequestHistories
                .OrderByDescending(history_record => history_record.RequestDate)
                .ToListAsync();
        }
    }
}