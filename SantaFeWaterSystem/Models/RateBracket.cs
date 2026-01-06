using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SantaFeWaterSystem.Models
{
    public class RateBracket
    {
        public int Id { get; set; }

        [Display(Name = "Consumer Type")]
        public ConsumerType? AccountType { get; set; }

        [Display(Name = "Minimum Cubic Meter")]
        public int? MinCubic { get; set; }

        [Display(Name = "Maximum Cubic Meter")]
        public int? MaxCubic { get; set; } // null = no upper limit

        [Display(Name = "Rate Per Cubic Meter")]
        [Range(0, double.MaxValue)]
        [Precision(18, 2)]
        public decimal? RatePerCubicMeter { get; set; }

        [Display(Name = "Base Amount")]
        [Range(0, double.MaxValue)]
        [Precision(18, 2)]
        public decimal? BaseAmount { get; set; }

        [Display(Name = "Penalty Amount")]
        [Range(0, double.MaxValue)]
        [Precision(18, 2)]
        public decimal? PenaltyAmount { get; set; }

        [Display(Name = "Effective Date")]
        [DataType(DataType.Date)]
        public DateTime? EffectiveDate { get; set; }
    }
}
