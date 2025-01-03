﻿using Infrastructures.UnitOfWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Models;
using Models.ViewModels;
using Stripe;
using System.Net.Sockets;
using Utilities.Utility;

namespace HotelReservation.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.AdminRole)]
    public class UserController : Controller
    {
        private readonly UserManager<IdentityUser> userManager;
        private readonly IUnitOfWork unitOfWork;
        private readonly RoleManager<IdentityRole> roleManager;
        private readonly ILogger<UserController> logger;

        public UserController(UserManager<IdentityUser> userManager, IUnitOfWork unitOfWork,RoleManager<IdentityRole> roleManager,ILogger<UserController> logger)
        {
            this.userManager = userManager;
            this.unitOfWork = unitOfWork;
            this.roleManager = roleManager;
            this.logger = logger;
        }

        // GET: UserController
        public async Task<IActionResult> Index(string? search, int pageNumber = 1)
        {
            const int pageSize = 10;
            var users = userManager.Users.ToList();
            var userViewModels = new List<UserViewModel>();

            foreach (var user in users)
            {
                var roles = await userManager.GetRolesAsync(user);
                userViewModels.Add(new UserViewModel
                {
                    User = (ApplicationUser)user,
                    Roles = roles
                });
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                userViewModels = userViewModels.Where(u =>
                    u.User.UserName.ToLower().Contains(search) ||
                    u.User.Email.ToLower().Contains(search) ||
                    (u.User.PhoneNumber != null && u.User.PhoneNumber.ToLower().Contains(search)) ||
                    u.Roles.Any(role => role.ToLower().Contains(search))
                ).ToList();
            }
            var totalItems = userViewModels.Count;
            var pagedUsers = userViewModels
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.SearchText = search;

            return View(pagedUsers);
        }

        // GET: UserController/Details/5
        public async Task<ActionResult> Lockout(string id)
        {
            var user = await userManager.FindByIdAsync(id);
            if (user != null)
            {
                if (await userManager.GetLockoutEndDateAsync(user) > DateTime.Now)
                {
                    // Unlock user
                    var unlockResult = await userManager.SetLockoutEndDateAsync(user, DateTime.Now.AddMinutes(-1));
                    if (!unlockResult.Succeeded)
                    {
                        foreach (var error in unlockResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error.Description);
                        }
                    }
                    Log(nameof(Lockout), nameof(User) + " " + $"unlocked {user.Email}");
                    TempData["success"] = "User unlocked successfully.";
                }
                else
                {
                    // Lock user
                    var lockResult = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                    if (!lockResult.Succeeded)
                    {
                        foreach (var error in lockResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error.Description);
                        }
                    }
                    Log(nameof(Lockout), nameof(User) + " " + $"lock {user.Email}");
                    TempData["success"] = "User locked successfully.";
                }
                return RedirectToAction(nameof(Index));
            }
            return RedirectToAction("NotFound", "Home", new { area = "Customer" });
        }


        // GET: UserController/Edit/5
        public async Task<ActionResult> Edit(string id)
        {
            var user = await userManager.FindByIdAsync(id);
            var appUser = user as ApplicationUser;

            if (appUser == null)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
            var role =  roleManager.Roles.ToList();
            ViewBag.Role = role;

            return View(appUser);
        }

        // POST: UserController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(ApplicationUser user,string role, IFormFile ProfileImage)
        {
            try
            {
                var existingUser = await userManager.FindByIdAsync(user.Id);
                var appUser = existingUser as ApplicationUser;
                if (appUser == null)
                {
                    return RedirectToAction("NotFound", "Home", new { area = "Customer" });
                }

                // Update user's profile image
                unitOfWork.UserRepository.UpdateProfileImage(appUser, ProfileImage);

                // Update other user properties
                appUser.Email = user.Email;
                appUser.UserName = user.Email;
                appUser.PhoneNumber = user.PhoneNumber;
                appUser.City = user.City; 
                var result = await userManager.UpdateAsync(appUser);
                if (result.Succeeded)
                {
                    var oldRole =await userManager.GetRolesAsync(appUser);
                    var removeResult= await userManager.RemoveFromRolesAsync(appUser,oldRole);
                    if (!removeResult.Succeeded)
                    {
                        return RedirectToAction("NotFound", "Home", new { area = "Customer" });
                    }
                    else
                    {
                        var addRoleResult = await userManager.AddToRoleAsync(appUser, role);
                        if (!addRoleResult.Succeeded)
                        {
                            return RedirectToAction("NotFound", "Home", new { area = "Customer" });
                        }
                    }

                    TempData["success"] = "User updated successfully.";
                    Log(nameof(Edit), nameof(User) + " " + $"{user.Email}");
                    return RedirectToAction(nameof(Index));
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty,error.Description);
                }
                return View(user); 
            }
            catch
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }
        public async Task<ActionResult> Delete(string id)
        {
            var user = await userManager.FindByIdAsync(id);
            if (user == null) {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
            var result=await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                TempData["Error"] = "Error";
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
            Log(nameof(Delete), nameof(User) + " " + $"lock {user.Email}");
            TempData["success"] = "User deleted successfully.";
            return RedirectToAction(nameof(Index));

        }
        public async void Log(string action, string entity)
        {
            LoggerHelper.LogAdminAction(logger, User.Identity.Name, action, entity);

        }
    }
}
