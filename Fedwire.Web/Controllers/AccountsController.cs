using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class AccountsController : Controller
{
    private readonly WireDbContext _context;

    public AccountsController(WireDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var accounts = await _context.Accounts
        .OrderByDescending(a => a.CreatedAt)
        .ToListAsync();

        return View(accounts);
    }
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountViewModel model)
    {
        var account = new Account
        {
            AccountId = Guid.NewGuid(),
            AccountNumber = model.AccountNumber,
            RoutingNumber = model.RoutingNumber,
            Balance = model.Balance,
            Currency = model.Currency,
            CreatedAt = DateTime.UtcNow
        };

        _context.Accounts.Add(account);

        await _context.SaveChangesAsync();

        return RedirectToAction("Index");
    }
}