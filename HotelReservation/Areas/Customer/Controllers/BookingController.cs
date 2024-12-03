﻿using Infrastructures.Repository;
using Infrastructures.Repository.IRepository;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Models.Models;
using Models.ViewModels;

using Stripe.Checkout;

namespace HotelReservation.Areas.Customer.Controllers
{
    [Area("Customer")]

    public class BookingController : Controller
    {
        private readonly IReservationRepository reservationRepository;
        private readonly UserManager<IdentityUser> userManager;
        private readonly IRepository<ReservationRoom> reservationRoomRepository;
        private readonly IHotelRepository hotelRepository;
        private readonly ICouponRepository couponRepository;
        private readonly IRoomTypeRepository roomTypeRepository;
        private readonly IRoomRepository roomRepository;

        public BookingController(IReservationRepository reservationRepository, UserManager<IdentityUser> userManager, IRepository<ReservationRoom> reservationRoomRepository, IHotelRepository hotelRepository, ICouponRepository couponRepository
            , IRoomTypeRepository roomTypeRepository, IRoomRepository roomRepository)
        {
            this.reservationRepository = reservationRepository;
            this.userManager = userManager;
            this.reservationRoomRepository = reservationRoomRepository;
            this.hotelRepository = hotelRepository;
            this.couponRepository = couponRepository;
            this.roomTypeRepository = roomTypeRepository;
            this.roomRepository = roomRepository;
        }

        [HttpGet]
        public IActionResult Filter(int hotelId, Models.Models.Type RoomType)
        {
            var rooms = roomRepository.Get(where: h => h.HotelId == hotelId
           && h.RoomType.Type ==RoomType
           , include: [e => e.RoomType]);


            //var roomsCount = rooms.Select(a => a.IsAvailable == true).Count();

            return View(rooms);
        }

        [HttpGet]
        public IActionResult Book(TypeViewModel typeModel)
        {
            var hotel = hotelRepository.GetOne(where: e => e.Id == typeModel.HotelId);
            if (hotel == null) return NotFound();

            int availableRoomsCount = roomRepository
        .Get(where: r => r.HotelId == typeModel.HotelId
                         && r.IsAvailable
                         && r.RoomType != null
                         && r.RoomType.Type == typeModel.RoomType
                         && r.RoomType.PricePN == typeModel.PricePN
                          && (r.RoomType.MealPrice == typeModel.MealPrice || (r.RoomType.MealPrice == null && typeModel.MealPrice == null)))
        .Count();


            ViewBag.Type = typeModel;
            ViewBag.availableRooms = availableRoomsCount;

            return View(hotel);

        }
        

        [HttpPost]
        public IActionResult Book(ReservationViewModel viewModel,TypeViewModel typeModel)
        {
            if (!ModelState.IsValid) return View(viewModel);

            var appUserId = userManager.GetUserId(User);
            if (appUserId == null) return RedirectToAction("Login", "Account");

            var availableRooms = roomRepository.Get(
                where: r => r.HotelId == typeModel.HotelId
                         && r.IsAvailable
                         && r.RoomType != null
                         && r.RoomType.Type == typeModel.RoomType
                         && r.RoomType.PricePN == typeModel.PricePN
                          && (r.RoomType.MealPrice == typeModel.MealPrice || (r.RoomType.MealPrice == null && typeModel.MealPrice == null)),
                           include: [r=>r.RoomType]).ToList();

            if (availableRooms == null || availableRooms.Count < viewModel.RoomCount)
            {
                TempData["ErrorMessage"] = "Not enough rooms are available. Please adjust your selection.";
                return RedirectToAction(nameof(Book), new { hotelId = viewModel.HotelId, roomType = viewModel.RoomType });
            }
            var selectedRoomType = availableRooms.First().RoomType;
            var totalPrice = selectedRoomType.PricePN * viewModel.RoomCount;

            if (viewModel.IncludesMeal && selectedRoomType.MealPrice.HasValue)
            {
                totalPrice += selectedRoomType.MealPrice.Value * viewModel.RoomCount;
            }

            if (!string.IsNullOrEmpty(viewModel.CouponCode))
            {
                var coupon = couponRepository.GetOne(where:c => c.Code == viewModel.CouponCode);
                if (coupon != null && coupon.Limit > 0)
                {
                    totalPrice -= (int)coupon.Discount;
                    coupon.Limit--;
                    couponRepository.Update(coupon);
                }
                else
                {
                    TempData["ErrorMessage"] = "Invalid or expired coupon.";
                    return View(viewModel);
                }
            }

            var reservation = new Reservation
            {
                HotelId = viewModel.HotelId,
                NAdult = viewModel.NAdult,
                NChildren = viewModel.NChildren ?? 0,
                CheckInDate = viewModel.CheckInDate,
                CheckOutDate = viewModel.CheckOutDate,
                RoomCount = viewModel.RoomCount,
                TotalPrice = totalPrice,
                UserId = appUserId
            };

            reservationRepository.Create(reservation);
            reservationRepository.Commit();

            for (int i = 0; i < viewModel.RoomCount; i++)
            {
                var room = availableRooms[i];
                var reservationRoom = new ReservationRoom
                {
                    RoomId = room.Id,
                    ReservationID = reservation.Id
                };
                room.IsAvailable = false;
                reservationRoomRepository.Create(reservationRoom);
                roomRepository.Update(room);
            }

            reservationRoomRepository.Commit();
            roomRepository.Commit();

            TempData["SuccessMessage"] = "Booking successful!";
            return RedirectToAction(nameof(Pay), new { reservationId = reservation.Id });
        }

        public IActionResult Pay(int reservationId)
        {
            var appUser = userManager.GetUserId(User);
            var reservations = reservationRepository.Get(
             include: [r => r.ReservationRooms,r=>r.Hotel],
             where: r => r.UserId == appUser && r.Id==reservationId
             ).ToList();
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>(),
                Mode = "payment",
                SuccessUrl = $"{Request.Scheme}://{Request.Host}/Customer/Booking/CheckOutSuccess",
                CancelUrl = $"{Request.Scheme}://{Request.Host}/Customer/Booking/CancelCheckout",
            };

            foreach (var item in reservations)
            {
                var result = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "egp",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Hotel: {item.Hotel.Name}, Rooms: {item.RoomCount}"
                        },
                        UnitAmountDecimal = (decimal?)(item.TotalPrice * 100),
                    },
                    Quantity = 1,
                };
                options.LineItems.Add(result);
            }

            var service = new SessionService();
            var session = service.Create(options);

            return Redirect(session.Url);
        }
        public IActionResult CheckOutSuccess()
        {
            var appUser = userManager.GetUserId(User);

            var reservations = reservationRepository.Get(
                        where: r => r.UserId == appUser
                        ).ToList();

            foreach (var reservation in reservations)
            {
                 
                //RoomTypeRepository.Update(reservation);
            }
            //RoomTypeRepository.Commit();

            TempData["Success"] = "Payment successful! Your reservations have been confirmed.";
            return View();

        }
        public IActionResult CancelCheckout()
        {
            TempData["Error"] = "Your payment was canceled. Please try again if you'd like to complete your booking.";
            return View();
        }


    }
}