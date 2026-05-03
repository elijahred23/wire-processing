using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

public class DashboardController : Controller
{
    private readonly WireDbContext _context;

    public DashboardController(WireDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var data = await _context.WireTransactions
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WireDashboardItem
            {
                WireTransactionId = w.WireTransactionId,
                ClientReferenceId = w.ClientReferenceId,
                Amount = w.Amount,
                CurrencyCode = w.CurrencyCode,
                Status = w.Status,
                Direction = w.Direction,
                CreatedAt = w.CreatedAt,

                LastMessageType = w.IsoMessages
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.MessageType)
                .FirstOrDefault(),

                LastDirection = w.IsoMessages
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.Direction)
                .FirstOrDefault(),

                CorrelationId = w.IsoMessages
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.CorrelationId)
                .FirstOrDefault()
            }).ToListAsync();

        return View(data);
    }
}