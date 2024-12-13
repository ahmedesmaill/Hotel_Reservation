﻿using Infrastructures.Repository;
using Infrastructures.Repository.IRepository;
using Infrastructures.UnitOfWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Models.Models;
using Utilities.Profles;
using Utilities.Utility;

namespace HotelReservation.Areas.Company.Controllers
{
    [Area("Company")]
    [Authorize(Roles = SD.CompanyRole)]
    public class HotelsController : Controller
    {
        private readonly IUnitOfWork unitOfWork;
        private readonly UserManager<IdentityUser> userManager;
        public HotelsController(IUnitOfWork unitOfWork, UserManager<IdentityUser> userManager)
        {
            this.unitOfWork = unitOfWork;
            this.userManager = userManager;
        }

        // GET: HotelsController
        public IActionResult Index(string? search, int pageNumber = 1)
        {
            try
            {
                var userName = userManager.GetUserName(User);

                if (string.IsNullOrEmpty(userName))
                {
                    return RedirectToAction("Index", "Home", new { area = "Customer" });
                }

                var company = unitOfWork.CompanyRepository.GetOne(where: e => e.UserName == userName);

                if (company == null)
                {
                    return RedirectToAction("NotFound", "Home", new { area = "Customer" });
                }

                var hotels = unitOfWork.HotelRepository.Get(where: e => e.CompanyId == company.Id);
                const int pageSize = 8;
                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();
                    hotels = hotels.Where(c =>
                        c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        c.Address.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        c.City.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        c.company.UserName.Contains(search, StringComparison.OrdinalIgnoreCase));
                }
                var totalItems = hotels.Count();
                var pagedHotels = hotels
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = pageNumber;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalItems = totalItems;
                ViewBag.SearchText = search;
                return View(pagedHotels.ToList());
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult Details(int id)
        {
            try
            {
                var user = userManager.GetUserId(User);
                var Hotels = unitOfWork.HotelRepository.Get(
                 [e => e.HotelAmenities, e => e.Rooms, e => e.ImageLists],
                    where: e => e.CompanyId.ToString() == user && e.Id == id);
                return View(Hotels);
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult Create()
        {
            try
            {
                var userName = userManager.GetUserName(User);
                var company = unitOfWork.CompanyRepository.GetOne(where: e => e.UserName == userName);
                if (company == null)
                {
                    ModelState.AddModelError("", "No company found for this user.");
                    return View();
                }

                var hotel = new Hotel
                {
                    CompanyId = company.Id,

                };

                ViewData["CompanyId"] = company?.Id;

                return View(hotel);
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        // POST: HotelsController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Hotel hotel, IFormFile ImgFile)
        {
            try
            {
                ModelState.Remove(nameof(ImgFile));
                if (ModelState.IsValid)
                {
                    var userId = userManager.GetUserName(User);
                    var company = unitOfWork.CompanyRepository.GetOne(where: e => e.UserName == userId);

                    if (company == null)
                    {
                        ModelState.AddModelError("", "Invalid Company.");
                        return View(hotel);
                    }

                    hotel.CompanyId = company.Id;

                    unitOfWork.HotelRepository.CreateWithImage(hotel, ImgFile, "homeImage", "CoverImg");
                    TempData["success"] = "Hotel created successfully.";
                    return RedirectToAction(nameof(Index));
                }

                return View(hotel);
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult Edit(int id)
        {
            try
            {
                var user = userManager.GetUserName(User);
                var company = unitOfWork.CompanyRepository.GetOne(where: e => e.UserName == user);
                var hotel = new Hotel
                {
                    CompanyId = company.Id,
                    ReportId = null
                };

                ViewData["CompanyId"] = company?.Id;

                var Hotel = unitOfWork.HotelRepository.GetOne(where: e => e.Id == id);
                return View(Hotel);
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        // POST: HotelsController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(Hotel hotel, IFormFile ImgFile)
        {
            try
            {
                ModelState.Remove(nameof(ImgFile));
                if (ModelState.IsValid)
                {
                    var oldHotel = unitOfWork.HotelRepository.GetOne(where: e => e.Id == hotel.Id);
                    if (oldHotel == null) return RedirectToAction("NotFound", "Home", new { area = "Customer" });
                    unitOfWork.HotelRepository.UpdateImage(hotel, ImgFile, oldHotel.CoverImg, "homeImage", "CoverImg");
                    TempData["success"] = "Hotel updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                return View(hotel);
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult Delete(int id)
        {
            try
            {
                var oldHotel = unitOfWork.HotelRepository.GetOne(where: e => e.Id == id);
                if (oldHotel != null)
                {
                    unitOfWork.ImageListRepository.DeleteHotelFolder(oldHotel.ImageLists, oldHotel.Name);
                }
                unitOfWork.HotelRepository.DeleteWithImage(oldHotel, "homeImage", oldHotel.CoverImg);

                unitOfWork.Complete();
                TempData["success"] = "Hotel deleted successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult ImageList(int hotelId)
        {
            try
            {
                if (hotelId != 0)
                {
                    Response.Cookies.Append("HotelId", hotelId.ToString());
                }
                if (hotelId == 0)
                {
                    hotelId = int.Parse(Request.Cookies["HotelId"]);
                }
                ViewBag.HotelId = hotelId;
                ViewBag.HotelName = unitOfWork.HotelRepository.GetOne(where: n => n.Id == hotelId)?.Name;
                var imgs = unitOfWork.ImageListRepository.Get(where: p => p.HotelId == hotelId);
                return View(imgs);
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult CreateImgList(int hotelId)
        {
            try
            {
                ViewBag.HotelId = hotelId;
                return View();
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateImgList(ImageList imageList, ICollection<IFormFile> ImgUrl)
        {
            try
            {
                var hotel = unitOfWork.HotelRepository.GetOne(where: e => e.Id == imageList.HotelId, tracked: false);
                unitOfWork.ImageListRepository.CreateImagesList(imageList, ImgUrl, hotel.Name);
                TempData["success"] = "Images added successfully.";

                return RedirectToAction(nameof(ImageList));
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult DeleteImgList(int id)
        {
            try
            {
                var img = unitOfWork.ImageListRepository.GetOne(where: e => e.Id == id, tracked: false);
                var hotel = unitOfWork.HotelRepository.GetOne(where: e => e.Id == img.HotelId, tracked: false);
                unitOfWork.ImageListRepository.DeleteImageList(id, hotel.Name);
                return RedirectToAction(nameof(ImageList));
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }

        public ActionResult DeleteAllImgList(int id)
        {
            try
            {
                var hotel = unitOfWork.HotelRepository.GetOne(include: [e => e.ImageLists], where: e => e.Id == id, tracked: false);
                unitOfWork.ImageListRepository.DeleteHotelFolder(hotel.ImageLists, hotel.Name);
                TempData["success"] = "All images deleted successfully.";
                return RedirectToAction(nameof(ImageList));
            }
            catch (Exception)
            {
                return RedirectToAction("NotFound", "Home", new { area = "Customer" });
            }
        }
    }
}
