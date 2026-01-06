using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using SantaFeWaterSystem.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff")]
    public class RateBracketController : BaseController
    {
        private readonly PermissionService _permissionService;
        private readonly AuditLogService _audit;

        public RateBracketController(
            ApplicationDbContext context,
            PermissionService permissionService,
            AuditLogService audit) : base(permissionService, context, audit)
        {
            _permissionService = permissionService;
            _audit = audit;
        }

        // INDEX
        public async Task<IActionResult> Index()
        {
            var list = await _context.RateBrackets
                .OrderBy(r => r.AccountType)
                .ThenBy(r => r.MinCubic)
                .ToListAsync();

            return View(list);
        }

        // GET: Create
        public IActionResult Create()
        {
            PopulateAccountTypesDropdown();

            var latest = _context.RateBrackets
                .OrderByDescending(r => r.EffectiveDate ?? DateTime.MinValue)
                .FirstOrDefault();

            var model = new RateBracket
            {
                AccountType = latest?.AccountType ?? ConsumerType.Residential,
                MinCubic = latest?.MinCubic ?? 0,
                MaxCubic = latest?.MaxCubic ?? 10,
                RatePerCubicMeter = latest?.RatePerCubicMeter ?? 0m,
                BaseAmount = latest?.BaseAmount ?? 130m,
                PenaltyAmount = latest?.PenaltyAmount ?? 0m,
                EffectiveDate = latest?.EffectiveDate ?? DateTime.Today
            };

            return View(model);
        }

        // POST: Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RateBracket rateBracket)
        {
            PopulateAccountTypesDropdown();

            // Basic validation
            if (rateBracket.MinCubic.HasValue && rateBracket.MaxCubic.HasValue
                && rateBracket.MaxCubic.Value < rateBracket.MinCubic.Value)
            {
                ModelState.AddModelError(nameof(rateBracket.MaxCubic), "Maximum cubic must be greater than or equal to Minimum cubic.");
            }

            if (!ModelState.IsValid)
                return View(rateBracket);

            // Duplicate check: same AccountType + same range + same EffectiveDate
            bool exists = _context.RateBrackets.Any(r =>
                r.AccountType == rateBracket.AccountType
                && r.MinCubic == rateBracket.MinCubic
                && r.MaxCubic == rateBracket.MaxCubic
                && (r.EffectiveDate == rateBracket.EffectiveDate));

            if (exists)
            {
                ModelState.AddModelError(string.Empty, "A rate bracket for this account type, range and effective date already exists.");
                return View(rateBracket);
            }

            _context.Add(rateBracket);
            await _context.SaveChangesAsync();

            // Audit
            var audit = new AuditTrail
            {
                Action = "Create Rate Bracket",
                PerformedBy = User.Identity?.Name ?? "Unknown",
                Timestamp = DateTime.Now,
                Details =
                    $"Created Rate Bracket:\n" +
                    $"- Account Type: {rateBracket.AccountType}\n" +
                    $"- Range: {rateBracket.MinCubic} to {(rateBracket.MaxCubic?.ToString() ?? "∞")}\n" +
                    $"- Rate/cbm: {rateBracket.RatePerCubicMeter:C}\n" +
                    $"- Base Amount: {rateBracket.BaseAmount:C}\n" +
                    $"- Penalty Amount: {rateBracket.PenaltyAmount:C}\n" +
                    $"- Effective Date: {rateBracket.EffectiveDate:yyyy-MM-dd}"
            };

            _context.AuditTrails.Add(audit);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Rate bracket successfully created.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Edit
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var rb = await _context.RateBrackets.FindAsync(id);
            if (rb == null) return NotFound();

            PopulateAccountTypesDropdown();
            return View(rb);
        }

        // POST: Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, RateBracket rateBracket)
        {
            if (id != rateBracket.Id) return NotFound();

            PopulateAccountTypesDropdown();

            if (rateBracket.MinCubic.HasValue && rateBracket.MaxCubic.HasValue
                && rateBracket.MaxCubic.Value < rateBracket.MinCubic.Value)
            {
                ModelState.AddModelError(nameof(rateBracket.MaxCubic), "Maximum cubic must be greater than or equal to Minimum cubic.");
            }

            if (!ModelState.IsValid) return View(rateBracket);

            // Duplicate check (exclude self)
            bool exists = _context.RateBrackets.Any(r =>
                r.AccountType == rateBracket.AccountType
                && r.MinCubic == rateBracket.MinCubic
                && r.MaxCubic == rateBracket.MaxCubic
                && r.EffectiveDate == rateBracket.EffectiveDate
                && r.Id != rateBracket.Id);

            if (exists)
            {
                ModelState.AddModelError(string.Empty, "Another rate bracket with the same account type, range and effective date exists.");
                return View(rateBracket);
            }

            try
            {
                var old = await _context.RateBrackets.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rateBracket.Id);

                _context.Update(rateBracket);
                await _context.SaveChangesAsync();

                var audit = new AuditTrail
                {
                    Action = "Edit Rate Bracket",
                    PerformedBy = User.Identity?.Name ?? "Unknown",
                    Timestamp = DateTime.Now,
                    Details =
                        $"Edited Rate Bracket ID: {rateBracket.Id}\n" +
                        $"Old Range: {old?.MinCubic} - {(old?.MaxCubic?.ToString() ?? "∞")}, New: {rateBracket.MinCubic} - {(rateBracket.MaxCubic?.ToString() ?? "∞")}\n" +
                        $"Old Rate: {old?.RatePerCubicMeter:C}, New: {rateBracket.RatePerCubicMeter:C}\n" +
                        $"Old Base: {old?.BaseAmount:C}, New: {rateBracket.BaseAmount:C}\n" +
                        $"Old Penalty: {old?.PenaltyAmount:C}, New: {rateBracket.PenaltyAmount:C}\n" +
                        $"Old EffectiveDate: {old?.EffectiveDate:yyyy-MM-dd}, New: {rateBracket.EffectiveDate:yyyy-MM-dd}"
                };

                _context.AuditTrails.Add(audit);
                await _context.SaveChangesAsync();

                TempData["Message"] = "Rate bracket updated.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!RateBracketExists(rateBracket.Id))
                    return NotFound();
                else
                    throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Delete
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var rb = await _context.RateBrackets.FirstOrDefaultAsync(r => r.Id == id);
            if (rb == null) return NotFound();
            return View(rb);
        }

        // POST: Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var rb = await _context.RateBrackets.FindAsync(id);
            if (rb != null)
            {
                string details =
                    $"Deleted Rate Bracket:\n- Account Type: {rb.AccountType}\n- Range: {rb.MinCubic} to {(rb.MaxCubic?.ToString() ?? "∞")}\n" +
                    $"- RatePerCbm: {rb.RatePerCubicMeter:C}\n- Base: {rb.BaseAmount:C}\n- Penalty: {rb.PenaltyAmount:C}\n- EffectiveDate: {rb.EffectiveDate:yyyy-MM-dd}";

                _context.RateBrackets.Remove(rb);
                await _context.SaveChangesAsync();

                var audit = new AuditTrail
                {
                    Action = "Delete Rate Bracket",
                    PerformedBy = User.Identity?.Name ?? "Unknown",
                    Timestamp = DateTime.Now,
                    Details = details
                };
                _context.AuditTrails.Add(audit);
                await _context.SaveChangesAsync();

                TempData["Message"] = "Rate bracket deleted.";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool RateBracketExists(int id)
        {
            return _context.RateBrackets.Any(e => e.Id == id);
        }

        private void PopulateAccountTypesDropdown()
        {
            var accountTypes = Enum.GetValues(typeof(ConsumerType))
                .Cast<ConsumerType>()
                .Select(at => new SelectListItem
                {
                    Value = at.ToString(),
                    Text = at.ToString()
                })
                .ToList();

            ViewBag.AccountTypes = accountTypes;
        }
    }
}
