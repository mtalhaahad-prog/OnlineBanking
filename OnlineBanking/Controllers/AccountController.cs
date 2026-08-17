using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using OnlineBanking.Models;
using System.Web.Helpers;

namespace OnlineBanking.Controllers
{
    public class AccountController : Controller
    {
        public OnlineBankingDBEntities db = new OnlineBankingDBEntities();

        // GET: Account/Login
        public ActionResult Login()
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

            if (Session["UserID"] != null)
            {
                var user = db.Users.Find((int)Session["UserID"]);
                if (user != null)
                {
                    if (user.AccountStatus == "Pending")
                    {
                        return RedirectToAction("Pending", new { id = user.UserID });
                    }
                    if (user.UserType == "Admin")
                    {
                        return RedirectToAction("Index", "Admin");
                    }
                    return RedirectToAction("Index", "Users");
                }
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(string email, string password, bool rememberMe = false)
        {
            var user = db.Users.FirstOrDefault(u => u.Email == email);

            bool isValidPassword = false;
            if (user != null)
            {
                try
                {
                    isValidPassword = Crypto.VerifyHashedPassword(user.PasswordHash, password);
                }
                catch (FormatException)
                {
                    isValidPassword = false;
                }
            }

            if (user == null || !isValidPassword)
            {
                ViewBag.LoginError = "Invalid email or password.";
                return View();
            }

            if (user.IsAccountLocked == true)
            {
                ViewBag.LoginError = "Your account is locked. Please contact support.";
                return View();
            }

            user.LastLoginDate = DateTime.Now;
            db.SaveChanges();

            // SESSION SET
            Session["UserID"] = user.UserID;
            Session["UserEmail"] = user.Email;
            Session["UserType"] = user.UserType;

            // REMEMBER ME — 30 din ke liye cookie
            if (rememberMe)
            {
                Response.Cookies.Add(new HttpCookie("RememberMe", user.UserID.ToString())
                {
                    Expires = DateTime.Now.AddDays(30),
                    HttpOnly = true
                });
            }

            if (user.AccountStatus == "Pending")
            {
                return RedirectToAction("Pending", new { id = user.UserID });
            }

            if (user.UserType == "Admin")
            {
                return RedirectToAction("Index", "Admin");
            }
            return RedirectToAction("Index", "Users");
        }

        // GET: Account/Register
        public ActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(User model, string confirmPassword)
        {
            if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.FirstName) ||
                string.IsNullOrEmpty(model.LastName) || string.IsNullOrEmpty(model.PasswordHash) ||
                string.IsNullOrEmpty(model.PhoneNumber) || string.IsNullOrEmpty(model.CNIC) ||
                string.IsNullOrEmpty(model.Address) || string.IsNullOrEmpty(model.City) ||
                string.IsNullOrEmpty(model.PostalCode) || string.IsNullOrEmpty(model.Country) ||
                model.DateOfBirth == null)
            {
                ViewBag.RegisterError = "Please fill all required fields.";
                return View(model);
            }

            if (model.PasswordHash != confirmPassword)
            {
                ViewBag.RegisterError = "Passwords do not match.";
                return View(model);
            }

            if (db.Users.Any(u => u.Email == model.Email))
            {
                ViewBag.RegisterError = "An account with this email already exists.";
                return View(model);
            }

            if (db.Users.Any(u => u.CNIC == model.CNIC))
            {
                ViewBag.RegisterError = "An account with this CNIC already exists.";
                return View(model);
            }

            var newUser = new User
            {
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                PasswordHash = Crypto.HashPassword(model.PasswordHash),
                PhoneNumber = model.PhoneNumber,
                Address = model.Address,
                City = model.City,
                PostalCode = model.PostalCode,
                Country = model.Country,
                DateOfBirth = model.DateOfBirth,
                CNIC = model.CNIC,
                UserType = "User",
                AccountStatus = "Pending",
                IsAccountLocked = false,
                IsEmailVerified = false,
                FailedLoginAttempts = 0,
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };

            db.Users.Add(newUser);
            db.SaveChanges();

            // SESSION SET — naya account banate hi login ho jaye
            Session["UserID"] = newUser.UserID;
            Session["UserEmail"] = newUser.Email;
            Session["UserType"] = newUser.UserType;

            return RedirectToAction("Pending", new { id = newUser.UserID });
        }

        // GET: Account/Pending
        public ActionResult Pending(int id)
        {
            var user = db.Users.Find(id);
            if (user == null)
            {
                return HttpNotFound();
            }

            if (user.AccountStatus != "Pending")
            {
                if (user.UserType == "Admin")
                {
                    return RedirectToAction("Index", "Admin");
                }
                return RedirectToAction("Index", "Users");
            }

            return View(user);
        }

        // GET: Account/Logout
        public ActionResult Logout()
        {
            Session.Clear();
            Session.Abandon();

            if (Request.Cookies["RememberMe"] != null)
            {
                Response.Cookies.Add(new HttpCookie("RememberMe", "")
                {
                    Expires = DateTime.Now.AddDays(-1)
                });
            }

            return RedirectToAction("Login");
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
}