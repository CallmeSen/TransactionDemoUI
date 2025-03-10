using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TransactionDemoUI.Controllers
{
    public class DemoController : Controller
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public DemoController(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Atomicity()
        {
            var log = new List<string>();
            using (var context = _contextFactory.CreateDbContext())
            {
                // Reset database to a known state
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE Accounts SET Balance = 100 WHERE AccountID IN ('Account1', 'Account2')");
                log.Add("Database reset: Account1 and Account2 balances set to 100");

                // Demonstrate atomicity: Transfer that fails due to insufficient funds
                try
                {
                    using (var transaction = await context.Database.BeginTransactionAsync())
                    {
                        var account1 = await context.Accounts.FindAsync("Account1");
                        var account2 = await context.Accounts.FindAsync("Account2");

                        // Attempt to transfer 150
                        if (account1.Balance < 150)
                        {
                            throw new Exception("Insufficient funds");
                        }
                        account1.Balance -= 150;
                        account2.Balance += 150;
                        await context.SaveChangesAsync();
                        await transaction.CommitAsync();
                        log.Add("Transfer succeeded");
                    }
                }
                catch (Exception ex)
                {
                    log.Add("Transfer failed: " + ex.Message);
                    // Transaction rolls back automatically
                }

                // Show final state
                var finalAccount1 = await context.Accounts.FindAsync("Account1");
                var finalAccount2 = await context.Accounts.FindAsync("Account2");
                log.Add($"Final balances: Account1 = {finalAccount1.Balance}, Account2 = {finalAccount2.Balance}");
            }
            return View("Result", log);
        }

        public async Task<IActionResult> IsolationDirtyRead()
        {
            var log = new List<string>();
            using (var context1 = _contextFactory.CreateDbContext())
            using (var context2 = _contextFactory.CreateDbContext())
            {
                // Reset database
                await context1.Database.ExecuteSqlRawAsync(
                    "UPDATE Accounts SET Balance = 100 WHERE AccountID = 'Account1'");
                log.Add("Database reset: Account1 balance set to 100");

                // Task 1: Update balance but don’t commit
                var task1 = Task.Run(async () =>
                {
                    using (var transaction = await context1.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted))
                    {
                        var account = await context1.Accounts.FindAsync("Account1");
                        account.Balance -= 50;
                        await context1.SaveChangesAsync();
                        log.Add("Task 1: Updated Account1 balance to 50 (uncommitted)");
                        await Task.Delay(5000); // Simulate delay
                        transaction.Rollback();
                        log.Add("Task 1: Rolled back transaction");
                    }
                });

                // Task 2: Read uncommitted data
                var task2 = Task.Run(async () =>
                {
                    await Task.Delay(1000); // Wait for Task 1 to update
                    using (var transaction = await context2.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadUncommitted))
                    {
                        var account = await context2.Accounts.FindAsync("Account1");
                        log.Add($"Task 2: Read Account1 balance as {account.Balance} (dirty read)");
                    }
                });

                await Task.WhenAll(task1, task2);

                // Final state
                var finalAccount = await context1.Accounts.FindAsync("Account1");
                log.Add($"Final balance after rollback: Account1 = {finalAccount.Balance}");
            }
            return View("Result", log);
        }
    }
}
