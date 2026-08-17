using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using OnlineBanking.Models;
using System.Data.Entity;

namespace OnlineBanking.Controllers
{
    public class UsersController : Controller
    {
        public OnlineBankingDBEntities db = new OnlineBankingDBEntities();

        // Session khali ho to RememberMe cookie se restore karne ki koshish karta hai.
        // Return true = login OK, false = caller ko Login page pe redirect karna chahiye.
        private bool EnsureLoggedIn()
        {
            if (Session["UserID"] != null)
            {
                return true;
            }

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
                    return true;
                }
            }

            return false;
        }

        // GET: Users
        public ActionResult Index()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            var accounts = db.BankAccounts
                              .Where(a => a.UserID == currentUser.UserID)
                              .ToList();
            ViewBag.Accounts = accounts;
            ViewBag.TotalAccounts = accounts.Count;
            ViewBag.TotalBalance = accounts.Sum(a => (decimal?)a.CurrentBalance) ?? 0;

            ViewBag.PendingRequestsCount = db.ServiceRequests
                .Count(r => r.UserID == currentUser.UserID && r.Status == "Pending");

            var accountIds = accounts.Select(a => a.AccountID).ToList();

            ViewBag.ThisMonthTransactions = db.Transactions
                .Count(t => (accountIds.Contains(t.FromAccountID ?? 0) || accountIds.Contains(t.ToAccountID ?? 0))
                            && t.TransactionDate != null
                            && t.TransactionDate.Value.Month == DateTime.Now.Month
                            && t.TransactionDate.Value.Year == DateTime.Now.Year);

            ViewBag.RecentActivity = db.Transactions
                .Include(t => t.BankAccount.User)
                .Include(t => t.BankAccount1.User)
                .Where(t => accountIds.Contains(t.FromAccountID ?? 0) || accountIds.Contains(t.ToAccountID ?? 0))
                .OrderByDescending(t => t.TransactionDate)
                .Take(3)
                .ToList();

            return View();
        }

        // GET: Users/Accounts
        public ActionResult Accounts()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            var accounts = db.BankAccounts
                              .Where(a => a.UserID == currentUser.UserID)
                              .OrderBy(a => a.OpenDate)
                              .ToList();

            return View(accounts);
        }

        // GET: Users/Transfer
        public ActionResult Transfer()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            ViewBag.MyAccounts = db.BankAccounts
                .Where(a => a.UserID == currentUser.UserID && a.AccountStatus == "Active")
                .ToList();

            ViewBag.Beneficiaries = db.Beneficiaries
                .Where(b => b.UserID == currentUser.UserID)
                .ToList();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Transfer(int fromAccountId, string toType, string toValue, string manualAccountNumber, decimal amount, string description)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            var fromAccount = db.BankAccounts.Find(fromAccountId);

            if (fromAccount == null || amount <= 0)
            {
                TempData["TransferError"] = "Invalid transfer details.";
                return RedirectToAction("Transfer");
            }

            if (fromAccount.AvailableBalance < amount)
            {
                TempData["TransferError"] = "Insufficient balance.";
                return RedirectToAction("Transfer");
            }

            BankAccount toAccount = null;

            if (toType == "own")
            {
                int toAccountId = int.Parse(toValue);
                toAccount = db.BankAccounts.Find(toAccountId);
            }
            else if (toType == "beneficiary")
            {
                int beneficiaryId = int.Parse(toValue);
                var beneficiary = db.Beneficiaries.Find(beneficiaryId);
                if (beneficiary != null)
                {
                    toAccount = db.BankAccounts.FirstOrDefault(a => a.AccountNumber == beneficiary.BeneficiaryAccountNumber);
                }
            }
            else if (toType == "manual")
            {
                if (string.IsNullOrEmpty(manualAccountNumber))
                {
                    TempData["TransferError"] = "Please enter a valid account number.";
                    return RedirectToAction("Transfer");
                }
                toAccount = db.BankAccounts.FirstOrDefault(a => a.AccountNumber == manualAccountNumber);

                if (toAccount == null)
                {
                    TempData["TransferError"] = "No account found with that account number.";
                    return RedirectToAction("Transfer");
                }
            }

            var transaction = new Transaction
            {
                FromAccountID = fromAccount.AccountID,
                ToAccountID = toAccount?.AccountID,
                TransactionType = "Transfer",
                Amount = amount,
                Currency = fromAccount.Currency,
                TransactionStatus = "Completed",
                Description = string.IsNullOrEmpty(description) ? "Fund transfer" : description,
                TransactionDate = DateTime.Now,
                CreatedDate = DateTime.Now
            };
            db.Transactions.Add(transaction);

            fromAccount.CurrentBalance -= amount;
            fromAccount.AvailableBalance -= amount;
            fromAccount.UpdatedDate = DateTime.Now;

            if (toAccount != null)
            {
                toAccount.CurrentBalance += amount;
                toAccount.AvailableBalance += amount;
                toAccount.UpdatedDate = DateTime.Now;
            }

            db.SaveChanges();

            TempData["TransferSuccess"] = "Funds transferred successfully.";
            return RedirectToAction("Transfer");
        }

        // POST: Users/AddBeneficiary
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddBeneficiary(string beneficiaryName, string beneficiaryAccountNumber, string relationship)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];

            if (string.IsNullOrEmpty(beneficiaryName) || string.IsNullOrEmpty(beneficiaryAccountNumber))
            {
                TempData["TransferError"] = "Please fill all beneficiary fields.";
                return RedirectToAction("Transfer");
            }

            var beneficiary = new Beneficiary
            {
                UserID = userId,
                BeneficiaryName = beneficiaryName,
                BeneficiaryAccountNumber = beneficiaryAccountNumber,
                Relationship = relationship,
                IsFavorite = false,
                CreatedDate = DateTime.Now
            };

            db.Beneficiaries.Add(beneficiary);
            db.SaveChanges();

            TempData["TransferSuccess"] = "Beneficiary added successfully.";
            return RedirectToAction("Transfer");
        }

        public ActionResult Transactions()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            var accountIds = db.BankAccounts
                .Where(a => a.UserID == currentUser.UserID)
                .Select(a => a.AccountID)
                .ToList();

            var transactions = db.Transactions
                .Include(t => t.BankAccount.User)
                .Include(t => t.BankAccount1.User)
                .Where(t => accountIds.Contains(t.FromAccountID ?? 0) || accountIds.Contains(t.ToAccountID ?? 0))
                .OrderByDescending(t => t.TransactionDate)
                .ToList();

            ViewBag.MyAccountIds = accountIds;

            return View(transactions);
        }

        // GET: Users/Statements
        public ActionResult Statements()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            var myAccounts = db.BankAccounts
                .Where(a => a.UserID == currentUser.UserID && a.AccountStatus == "Active")
                .ToList();
            ViewBag.MyAccounts = myAccounts;

            var accountIds = myAccounts.Select(a => a.AccountID).ToList();

            var monthlyStatements = db.Transactions
                .Where(t => (accountIds.Contains(t.FromAccountID ?? 0) || accountIds.Contains(t.ToAccountID ?? 0))
                            && t.TransactionDate != null)
                .ToList()
                .GroupBy(t => new { t.TransactionDate.Value.Year, t.TransactionDate.Value.Month })
                .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
                .Select(g => new StatementSummary
                {
                    Period = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMMM yyyy"),
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TransactionCount = g.Count(),
                    TotalCredit = g.Where(t => accountIds.Contains(t.ToAccountID ?? 0)).Sum(t => t.Amount),
                    TotalDebit = g.Where(t => accountIds.Contains(t.FromAccountID ?? 0)).Sum(t => t.Amount)
                })
                .Take(6)
                .ToList();

            return View(monthlyStatements);
        }

        // GET: Users/GetStatementData (AJAX — PDF generate karne ke liye data)
        public JsonResult GetStatementData(int accountId, string fromDate, string toDate)
        {
            if (!EnsureLoggedIn())
            {
                return Json(new { error = "Not logged in" }, JsonRequestBehavior.AllowGet);
            }

            int userId = (int)Session["UserID"];
            var account = db.BankAccounts.FirstOrDefault(a => a.AccountID == accountId && a.UserID == userId);

            if (account == null)
            {
                return Json(new { error = "Account not found" }, JsonRequestBehavior.AllowGet);
            }

            DateTime from = DateTime.Parse(fromDate);
            DateTime to = DateTime.Parse(toDate).AddDays(1).AddSeconds(-1);

            // Pehle raw data database se le aao (koi ToString() yahan nahi)
            var rawTransactions = db.Transactions
                .Where(t => (t.FromAccountID == accountId || t.ToAccountID == accountId)
                            && t.TransactionDate >= from && t.TransactionDate <= to)
                .OrderBy(t => t.TransactionDate)
                .ToList();

            // Ab memory mein formatting karo
            var transactions = rawTransactions.Select(t => new
            {
                date = t.TransactionDate.Value.ToString("MMM dd, yyyy"),
                description = t.Description,
                type = t.FromAccountID == accountId ? "Debit" : "Credit",
                amount = t.Amount,
                status = t.TransactionStatus
            }).ToList();

            var currentUser = db.Users.Find(userId);

            return Json(new
            {
                accountHolder = currentUser.FirstName + " " + currentUser.LastName,
                accountNumber = account.AccountNumber,
                accountType = account.AccountType,
                currentBalance = account.CurrentBalance,
                transactions = transactions
            }, JsonRequestBehavior.AllowGet);
        }


        // GET: Users/Cheque
        public ActionResult Cheque()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            ViewBag.MyAccounts = db.BankAccounts
                .Where(a => a.UserID == currentUser.UserID && a.AccountStatus == "Active")
                .ToList();

            return View();
        }

        // POST: Users/Cheque
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cheque(int accountId, int leaves, string deliveryAddress)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];

            var account = db.BankAccounts.FirstOrDefault(a => a.AccountID == accountId && a.UserID == userId);
            if (account == null)
            {
                TempData["ChequeError"] = "Invalid account selected.";
                return RedirectToAction("Cheque");
            }

            var request = new ServiceRequest
            {
                UserID = userId,
                AccountID = accountId,
                RequestType = "Cheque Book",
                Status = "Pending",
                Priority = "Normal",
                ChequeNumber = leaves.ToString(),
                NewAddress = string.IsNullOrEmpty(deliveryAddress) ? null : deliveryAddress,
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };

            db.ServiceRequests.Add(request);
            db.SaveChanges();

            TempData["ChequeSuccess"] = "Your cheque book request has been submitted successfully.";
            return RedirectToAction("Cheque");
        }


        // GET: Users/StopPayment
        public ActionResult StopPayment()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            ViewBag.MyAccounts = db.BankAccounts
                .Where(a => a.UserID == currentUser.UserID && a.AccountStatus == "Active")
                .ToList();

            return View();
        }

        // POST: Users/StopPayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult StopPayment(int accountId, string chequeNumber, decimal amount)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];

            var account = db.BankAccounts.FirstOrDefault(a => a.AccountID == accountId && a.UserID == userId);
            if (account == null)
            {
                TempData["StopPaymentError"] = "Invalid account selected.";
                return RedirectToAction("StopPayment");
            }

            if (string.IsNullOrEmpty(chequeNumber) || amount <= 0)
            {
                TempData["StopPaymentError"] = "Please fill all fields correctly.";
                return RedirectToAction("StopPayment");
            }

            var request = new ServiceRequest
            {
                UserID = userId,
                AccountID = accountId,
                RequestType = "Stop Payment",
                Status = "Pending",
                Priority = "High",
                ChequeNumber = chequeNumber,
                ChequeAmount = amount,
                StopPaymentReason = "Requested by account holder",
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };

            db.ServiceRequests.Add(request);
            db.SaveChanges();

            TempData["StopPaymentSuccess"] = "Your stop payment request has been submitted successfully.";
            return RedirectToAction("StopPayment");
        }

        // GET: Users/GetAccountTransactions (AJAX)
        public JsonResult GetAccountTransactions(int accountId)
        {
            if (!EnsureLoggedIn())
            {
                return Json(new { error = "Not logged in" }, JsonRequestBehavior.AllowGet);
            }

            int userId = (int)Session["UserID"];
            var account = db.BankAccounts.FirstOrDefault(a => a.AccountID == accountId && a.UserID == userId);

            if (account == null)
            {
                return Json(new { error = "Account not found" }, JsonRequestBehavior.AllowGet);
            }

            var rawTransactions = db.Transactions
                .Include(t => t.BankAccount.User)
                .Include(t => t.BankAccount1.User)
                .Where(t => t.FromAccountID == accountId || t.ToAccountID == accountId)
                .OrderByDescending(t => t.TransactionDate)
                .Take(3)
                .ToList();

            var transactions = rawTransactions.Select(t => new
            {
                date = t.TransactionDate.HasValue ? t.TransactionDate.Value.ToString("MMM dd, yyyy hh:mm tt") : "-",
                fromName = t.BankAccount?.User != null ? (t.BankAccount.User.FirstName + " " + t.BankAccount.User.LastName) : "Admin",
                fromAccount = t.BankAccount?.AccountNumber ?? "-",
                toName = t.BankAccount1?.User != null ? (t.BankAccount1.User.FirstName + " " + t.BankAccount1.User.LastName) : "-",
                description = t.Description,
                type = t.FromAccountID == accountId ? "Debit" : "Credit",
                amount = t.Amount,
                status = t.TransactionStatus
            }).ToList();

            return Json(new { transactions = transactions }, JsonRequestBehavior.AllowGet);
        }


        // GET: Users/Support
        public ActionResult Support()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;

            var myTickets = db.SupportTickets
                .Where(t => t.UserID == userId)
                .OrderByDescending(t => t.CreatedDate)
                .ToList();

            return View(myTickets);
        }

        // POST: Users/Support
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Support(string subject, string message)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];

            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(message))
            {
                TempData["SupportError"] = "Please fill all fields.";
                return RedirectToAction("Support");
            }

            var ticketNumber = "TCK-" + DateTime.Now.ToString("yyMMddHHmmss");

            var ticket = new SupportTicket
            {
                UserID = userId,
                TicketNumber = ticketNumber,
                Subject = subject,
                Description = message,
                Category = "General",
                Priority = "Normal",
                Status = "Open",
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };

            db.SupportTickets.Add(ticket);
            db.SaveChanges();

            TempData["SupportSuccess"] = "Your message has been sent. Ticket #" + ticketNumber;
            return RedirectToAction("Support");
        }


        // GET: Users/Profile
        public ActionResult Profile()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var currentUser = db.Users.Find(userId);

            if (currentUser == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.CurrentUser = currentUser;
            return View(currentUser);
        }

        // POST: Users/UpdateProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateProfile(string firstName, string lastName, string phoneNumber)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var user = db.Users.Find(userId);

            if (user == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName) || string.IsNullOrEmpty(phoneNumber))
            {
                TempData["ProfileError"] = "Please fill all fields.";
                return RedirectToAction("Profile");
            }

            user.FirstName = firstName;
            user.LastName = lastName;
            user.PhoneNumber = phoneNumber;
            user.UpdatedDate = DateTime.Now;
            db.SaveChanges();

            TempData["ProfileSuccess"] = "Profile updated successfully.";
            return RedirectToAction("Profile");
        }

        // POST: Users/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];
            var user = db.Users.Find(userId);

            if (user == null)
            {
                Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            bool isValidCurrent = false;
            try
            {
                isValidCurrent = System.Web.Helpers.Crypto.VerifyHashedPassword(user.PasswordHash, currentPassword);
            }
            catch (FormatException)
            {
                isValidCurrent = false;
            }

            if (!isValidCurrent)
            {
                TempData["PasswordError"] = "Current password is incorrect.";
                return RedirectToAction("Profile");
            }

            if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword)
            {
                TempData["PasswordError"] = "New password and confirm password do not match.";
                return RedirectToAction("Profile");
            }

            user.PasswordHash = System.Web.Helpers.Crypto.HashPassword(newPassword);
            user.UpdatedDate = DateTime.Now;
            db.SaveChanges();

            TempData["PasswordSuccess"] = "Password updated successfully.";
            return RedirectToAction("Profile");
        }

        // POST: Users/RequestContactChange
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RequestContactChange(string changeType, string newEmail, string newAddress, string newCity)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = (int)Session["UserID"];

            if (changeType == "Email")
            {
                if (string.IsNullOrEmpty(newEmail))
                {
                    TempData["ProfileError"] = "Please enter a valid email.";
                    return RedirectToAction("Profile");
                }

                if (db.Users.Any(u => u.Email == newEmail && u.UserID != userId))
                {
                    TempData["ProfileError"] = "This email is already in use.";
                    return RedirectToAction("Profile");
                }

                var request = new ServiceRequest
                {
                    UserID = userId,
                    RequestType = "Email Change",
                    Status = "Pending",
                    Priority = "Normal",
                    NewAddress = newEmail, // naya email yahan store ho raha hai (reuse ho raha field hai)
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                };
                db.ServiceRequests.Add(request);
            }
            else if (changeType == "Address")
            {
                if (string.IsNullOrEmpty(newAddress) || string.IsNullOrEmpty(newCity))
                {
                    TempData["ProfileError"] = "Please fill all address fields.";
                    return RedirectToAction("Profile");
                }

                var request = new ServiceRequest
                {
                    UserID = userId,
                    RequestType = "Address Change",
                    Status = "Pending",
                    Priority = "Normal",
                    NewAddress = newAddress,
                    NewCity = newCity,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                };
                db.ServiceRequests.Add(request);
            }
            else
            {
                TempData["ProfileError"] = "Please select a valid change type.";
                return RedirectToAction("Profile");
            }

            db.SaveChanges();

            TempData["ProfileSuccess"] = "Your request has been submitted for admin approval.";
            return RedirectToAction("Profile");
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

    // Naya class — UsersController ke bahar, isi file/namespace ke andar
    public class StatementSummary
    {
        public string Period { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int TransactionCount { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal TotalDebit { get; set; }
    }
}