using FinanceTracker.Api.Data;
using FinanceTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
namespace FinanceTracker.Api.Services;
public static class DemoSeeder
{
    public static async Task Seed(ApplicationDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Username == "portfolio-demo")) return;
        var user = new AppUser { Username = "portfolio-demo", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo-Rands-2026!") };
        db.Users.Add(user); await db.SaveChangesAsync();
        var start = new DateTime(2026, 4, 1);
        for (var i = 0; i < 6; i++)
        {
            var month = start.AddMonths(i);
            db.MonthlyBudgets.Add(new MonthlyBudget { UserId = user.Id, Year = month.Year, Month = month.Month, LimitAmount = 18000,
                CategoryLimits = [new() { Category = "Rent", LimitAmount = 8000 }, new() { Category = "Groceries", LimitAmount = 4000 },
                    new() { Category = "Transport", LimitAmount = 1800 }, new() { Category = "Utilities", LimitAmount = 1600 }] });
            void Add(int day, string description, decimal amount, string category) => db.Transactions.Add(new Transaction { UserId = user.Id, Date = month.AddDays(day - 1), Description = description, Amount = amount, Category = category });
            Add(1, "Monthly salary", 26500, "Income");
            Add(2, "Cape Town apartment rent", -8000, "Rent");
            Add(3, "Checkers groceries", -1100 - i * 45, "Groceries");
            Add(7, "Prepaid electricity", -850 - i * 25, "Utilities");
            Add(10, "MyCiTi monthly travel", -680, "Transport");
            Add(14, "Pick n Pay weekly groceries", -875 - i * 20, "Groceries");
            Add(18, "Fibre internet", -699, "Utilities");
            Add(21, "Weekend restaurant", -420 - i * 35, "Food");
            Add(25, "Woolworths food", -725, "Groceries");
            Add(27, "Cinema and snacks", -240, "Entertainment");
        }
        await db.SaveChangesAsync();
    }
}
