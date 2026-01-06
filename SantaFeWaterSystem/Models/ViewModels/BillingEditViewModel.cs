using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SantaFeWaterSystem.Models;

namespace SantaFeWaterSystem.ViewModels
{
    public class BillingEditViewModel
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Bill No")]
        public string BillNo { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Consumer")]
        public int ConsumerId { get; set; }

        [Display(Name = "Billing Date")]
        [DataType(DataType.Date)]
        public DateTime BillingDate { get; set; }

        [Display(Name = "Previous Reading")]
        public decimal PreviousReading { get; set; }

        [Display(Name = "Present Reading")]
        public decimal PresentReading { get; set; }

        [Display(Name = "Cubic Meter Used")]
        public decimal CubicMeterUsed { get; set; }

        [Display(Name = "Amount Due")]
        public decimal AmountDue { get; set; }

        [Display(Name = "Penalty")]
        public decimal Penalty { get; set; }

        [Display(Name = "Additional Fees")]
        public decimal? AdditionalFees { get; set; }

        [Display(Name = "Total Amount")]
        public decimal TotalAmount { get; set; }

        [Display(Name = "Due Date")]
        [DataType(DataType.Date)]
        public DateTime? DueDate { get; set; }

        [Display(Name = "Status")]
        public string? Status { get; set; }

        // For dropdowns
        public IEnumerable<Consumer>? Consumers { get; set; }

        // maintain paging/filter state
        public int? Page { get; set; }
        public string? Search { get; set; }
        public string? StatusFilter { get; set; }
    }
}
