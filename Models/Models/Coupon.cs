﻿using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace Models.Models
{
    public class Coupon
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public int Limit { get; set; }
        [Range(0,99)]
        public double Discount { get; set; }
        public DateOnly ExpireDate { get; set; }
        [ValidateNever]
        public ICollection<Reservation> reservations { get; set; }
    }
}
