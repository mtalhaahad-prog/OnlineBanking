using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using OnlineBanking.Models;
using System.Data.Entity;

namespace OnlineBanking.Controllers
{
    public class AdminController : Controller
    {
        public OnlineBankingDBEntities db = new OnlineBankingDBEntities();

        private bool EnsureAdminLoggedIn()
        {
            if (Session["UserID"] == null)
            {
                var cookie = Request.Cookies["RememberMe"];
                int cookieUserId;
                if (cookie != null && int.TryParse(cookie.Value, out cookieUserId))
                {
                    var cookieUser = db.Users.Find(cookieUserId);
                    if (cookieUser != null)
                    {
                        Session["UserID"] = cookieUser.UserID;
                        Session["UserEmail"] = cookieUser.Email;
                        Session["UserType"] = cookieUser.UserType;
                    }
                }
            }

            if (Session["UserID"] == null)
            {
                return false;
            }

            var user = db.Users.Find((int)Session["UserID"]);
            return user != null && user.UserType == "Admin";
        }

        public ActionResult Index()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            ViewBag.TotalUsers = db.Users.Count();
            ViewBag.PendingRequestsCount = db.ServiceRequests.Count(r => r.Status == "Pending");
            ViewBag.TodaysTransactions = db.Transactions.Count(t =>
                t.TransactionDate != null &&
                DbFunctions.TruncateTime(t.TransactionDate) == DbFunctions.TruncateTime(DateTime.Now));

            ViewBag.RecentTransactions = db.Transactions
                .Include(t => t.BankAccount)
                .Include(t => t.BankAccount1)
                .OrderByDescending(t => t.TransactionDate)
                .Take(5)
                .ToList();

            ViewBag.RecentActivity = db.AuditLogs
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedDate)
                .Take(5)
                .ToList();

            return View();
        }

        public ActionResult Users()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var pendingUsers = db.Users
                                  .Where(u => u.AccountStatus == "Pending")
                                  .OrderByDescending(u => u.CreatedDate)
                                  .ToList();

            var otherUsers = db.Users
                                .Where(u => u.AccountStatus != "Pending")
                                .OrderByDescending(u => u.CreatedDate)
                                .ToList();

            ViewBag.PendingUsers = pendingUsers;
            return View(otherUsers);
        }

        private void CreateDefaultAccountIfNeeded(User user)
        {
            bool hasNoAccounts = !db.BankAccounts.Any(a => a.UserID == user.UserID);
            if (!hasNoAccounts) return;

            var random = new Random();
            string accountNumber = "ACC-" + user.UserID.ToString("D4") + "-" + random.Next(1000, 9999);
            string iban = "PK36OBNK" + user.UserID.ToString("D4") + random.Next(100000, 999999);

            var newAccount = new BankAccount
            {
                UserID = user.UserID,
                AccountNumber = accountNumber,
                IBAN = iban,
                AccountType = "Savings",
                Currency = "PKR",
                CurrentBalance = 0,
                AvailableBalance = 0,
                AccountStatus = "Active",
                OpenDate = DateTime.Now,
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };
            db.BankAccounts.Add(newAccount);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditUser(User model)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var user = db.Users.Find(model.UserID);
            if (user != null)
            {
                bool wasNotActive = user.AccountStatus != "Active";

                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                user.Email = model.Email;
                user.PhoneNumber = model.PhoneNumber;
                user.AccountStatus = model.AccountStatus;
                user.UserType = model.UserType;
                user.IsAccountLocked = model.IsAccountLocked;
                user.IsEmailVerified = model.IsEmailVerified;
                user.UpdatedDate = DateTime.Now;

                bool justActivated = wasNotActive && user.AccountStatus == "Active";
                if (justActivated)
                {
                    CreateDefaultAccountIfNeeded(user);
                }

                AuditLogger.Log(db, user.UserID, "Update", "User", user.UserID);
                db.SaveChanges();
            }
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ApproveUser(int id)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var user = db.Users.Find(id);
            if (user != null && user.AccountStatus == "Pending")
            {
                user.AccountStatus = "Active";
                user.UpdatedDate = DateTime.Now;

                CreateDefaultAccountIfNeeded(user);

                AuditLogger.Log(db, user.UserID, "Approve", "User", user.UserID);
                db.SaveChanges();
            }
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RejectUser(int id)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var user = db.Users.Find(id);
            if (user != null && user.AccountStatus == "Pending")
            {
                user.AccountStatus = "Inactive";
                user.UpdatedDate = DateTime.Now;

                AuditLogger.Log(db, user.UserID, "Reject", "User", user.UserID);
                db.SaveChanges();
            }
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteUser(int id)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var user = db.Users.Find(id);
            if (user != null)
            {
                AuditLogger.Log(db, user.UserID, "Delete", "User", user.UserID);
                db.Users.Remove(user);
                db.SaveChanges();
            }
            return RedirectToAction("Users");
        }

        public ActionResult Requests()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var requests = db.ServiceRequests
                              .Include(r => r.User1)
                              .Where(r => r.Status == "Pending")
                              .OrderByDescending(r => r.CreatedDate)
                              .ToList();
            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateRequestStatus(int id, string newStatus)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var request = db.ServiceRequests.Find(id);
            if (request != null)
            {
                request.Status = newStatus;
                request.UpdatedDate = DateTime.Now;

                if (newStatus == "Approved")
                {
                    request.ApprovedDate = DateTime.Now;

                    if (request.RequestType == "Email Change" && !string.IsNullOrEmpty(request.NewAddress))
                    {
                        var user = db.Users.Find(request.UserID);
                        if (user != null)
                        {
                            user.Email = request.NewAddress;
                            user.UpdatedDate = DateTime.Now;
                        }
                    }
                    else if (request.RequestType == "Address Change")
                    {
                        var user = db.Users.Find(request.UserID);
                        if (user != null)
                        {
                            user.Address = request.NewAddress;
                            user.City = request.NewCity;
                            user.UpdatedDate = DateTime.Now;
                        }
                    }
                }

                AuditLogger.Log(db, request.UserID, newStatus == "Approved" ? "Approve" : "Reject", "ServiceRequest", request.RequestID);
                db.SaveChanges();
            }

            var referrer = Request.UrlReferrer?.ToString();
            if (referrer != null && referrer.Contains("Approvals"))
            {
                return RedirectToAction("Approvals");
            }
            return RedirectToAction("Requests");
        }

        public ActionResult Approvals()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var decidedRequests = db.ServiceRequests
                                     .Include(r => r.User1)
                                     .Where(r => r.Status == "Approved" || r.Status == "Rejected")
                                     .OrderByDescending(r => r.UpdatedDate)
                                     .ToList();
            return View(decidedRequests);
        }

        public ActionResult Transactions()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var transactions = db.Transactions
                                  .Include(t => t.BankAccount.User)
                                  .Include(t => t.BankAccount1.User)
                                  .OrderByDescending(t => t.TransactionDate)
                                  .ToList();
            return View(transactions);
        }

        public ActionResult Reports()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            ViewBag.TotalUsers = db.Users.Count();
            ViewBag.TotalAccounts = db.BankAccounts.Count();
            ViewBag.TotalBalance = db.BankAccounts.Sum(a => (decimal?)a.CurrentBalance) ?? 0;
            ViewBag.TotalTransactions = db.Transactions.Count();
            ViewBag.PendingRequests = db.ServiceRequests.Count(r => r.Status == "Pending");

            ViewBag.TransactionsByType = db.Transactions
                .GroupBy(t => t.TransactionType)
                .Select(g => new ReportItem { Label = g.Key, Count = g.Count(), Amount = g.Sum(t => t.Amount) })
                .ToList();

            return View();
        }

        public ActionResult AuditLog()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var logs = db.AuditLogs
                         .Include(l => l.User)
                         .OrderByDescending(l => l.CreatedDate)
                         .ToList();
            return View(logs);
        }

        public ActionResult Support()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var tickets = db.SupportTickets
                             .Include(t => t.User1)
                             .OrderByDescending(t => t.CreatedDate)
                             .ToList();
            return View(tickets);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateTicketStatus(int id, string newStatus)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var ticket = db.SupportTickets.Find(id);
            if (ticket != null)
            {
                ticket.Status = newStatus;
                ticket.UpdatedDate = DateTime.Now;

                AuditLogger.Log(db, ticket.UserID, "Update", "SupportTicket", ticket.TicketID);
                db.SaveChanges();
            }
            return RedirectToAction("Support");
        }

        public ActionResult Accounts()
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var accounts = db.BankAccounts
                              .Include(a => a.User)
                              .OrderByDescending(a => a.CreatedDate)
                              .ToList();
            return View(accounts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddMoney(int id, decimal amount)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var account = db.BankAccounts.Find(id);
            if (account != null && amount > 0)
            {
                account.CurrentBalance += amount;
                account.AvailableBalance += amount;
                account.UpdatedDate = DateTime.Now;

                var transaction = new Transaction
                {
                    FromAccountID = null,
                    ToAccountID = account.AccountID,
                    TransactionType = "Deposit",
                    Amount = amount,
                    Currency = account.Currency,
                    TransactionStatus = "Completed",
                    Description = "Cash deposit by admin",
                    TransactionDate = DateTime.Now,
                    CreatedDate = DateTime.Now
                };
                db.Transactions.Add(transaction);

                AuditLogger.Log(db, account.UserID, "Deposit", "BankAccount", account.AccountID);
                db.SaveChanges();
            }
            return RedirectToAction("Accounts");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateAccountStatus(int id, string newStatus)
        {
            if (!EnsureAdminLoggedIn()) return RedirectToAction("Login", "Account");

            var account = db.BankAccounts.Find(id);
            if (account != null)
            {
                account.AccountStatus = newStatus;
                account.UpdatedDate = DateTime.Now;

                string actionLabel = newStatus == "Frozen" ? "Freeze"
                    : newStatus == "Active" ? "Activate"
                    : "Deactivate";
                AuditLogger.Log(db, account.UserID, actionLabel, "BankAccount", account.AccountID);

                db.SaveChanges();
            }
            return RedirectToAction("Accounts");
        }

        [ChildActionOnly]
        public ActionResult PendingUsersBadge() 
        {
            int count = db.Users.Count(u => u.AccountStatus == "Pending");
            ViewBag.Count = count;
            return PartialView();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class ReportItem
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public decimal Amount { get; set; }
    }
}