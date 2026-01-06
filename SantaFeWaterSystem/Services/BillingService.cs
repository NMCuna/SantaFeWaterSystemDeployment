// Services/BillingService.cs
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System.Text.Json;

namespace SantaFeWaterSystem.Services
{
    public class BillingService
    {
        private readonly ApplicationDbContext _context;

        // 🔹 Cached rate data to avoid reloading from DB every time
        private static Dictionary<ConsumerType, List<RateBracket>> _rateCache = new();
        private static DateTime _lastRateUpdate = DateTime.MinValue;

        public BillingService(ApplicationDbContext context)
        {
            _context = context;
        }

        // 🔹 Used to clear cache when admin updates rate brackets
        public static void InvalidateRateCache()
        {
            _rateCache.Clear();
            _lastRateUpdate = DateTime.Now;
            Console.WriteLine("[BillingService] Rate cache invalidated.");
        }

        // ================================
        // 🔹 Revert Payment Status
        // ================================
        public async Task RevertBillingPaymentStatus(int billingId)
        {
            var billing = await _context.Billings.FindAsync(billingId);
            if (billing != null)
            {
                billing.Status = "Unpaid";
                billing.IsPaid = false;
                await _context.SaveChangesAsync();
            }
        }

        // ================================
        // 🔹 Step 1: Compute bill using rate brackets
        // ================================
        public async Task<decimal> ComputeBillAsync(int consumption, ConsumerType accountType)
        {
            if (consumption < 0) consumption = 0;

            var brackets = await GetRateBracketsAsync(accountType);
            if (!brackets.Any())
                return 0m;

            decimal total = 0m;

            foreach (var b in brackets)
            {
                int min = b.MinCubic ?? 0;
                int max = b.MaxCubic ?? int.MaxValue;
                decimal rate = b.RatePerCubicMeter ?? 0m;
                decimal? baseAmt = b.BaseAmount;

                if (consumption < min) continue;

                if (baseAmt.HasValue)
                {
                    total += baseAmt.Value;
                }
                else
                {
                    int upper = Math.Min(consumption, max);
                    int units = upper - min + 1;
                    if (units > 0)
                    {
                        total += units * rate;
                    }
                }
            }

            return total;
        }

        // ================================
        // 🔹 Load Rate Brackets (with cache)
        // ================================
        private async Task<List<RateBracket>> GetRateBracketsAsync(ConsumerType accountType)
        {
            if (!_rateCache.ContainsKey(accountType))
            {
                _rateCache[accountType] = await _context.RateBrackets
                    .Where(r => r.AccountType == accountType)
                    .OrderBy(r => r.MinCubic ?? 0)
                    .ToListAsync();

                _lastRateUpdate = DateTime.Now;
                Console.WriteLine($"[BillingService] Rate cache refreshed for {accountType}");
            }

            return _rateCache[accountType];
        }

        // ================================
        // 🔹 Step 2: Apply penalty to one bill
        // ================================
        public async Task ApplyPenaltyAsync(Billing bill)
        {
            if (bill == null) return;

            var consumer = await _context.Consumers.FindAsync(bill.ConsumerId);
            if (consumer == null) return;

            // Get latest rate for this account type
            var rateRecord = (await GetRateBracketsAsync(consumer.AccountType))
                .Where(r => r.EffectiveDate <= bill.BillingDate)
                .OrderByDescending(r => r.EffectiveDate)
                .FirstOrDefault();

            decimal penaltyFromRate = rateRecord?.PenaltyAmount ?? 10m;

            // Apply penalty if overdue and unpaid
            if (bill.DueDate < DateTime.Today && bill.Status != "Paid")
            {
                bill.Penalty = penaltyFromRate;
            }
            else
            {
                bill.Penalty = 0m;
            }

            decimal additionalFees = bill.AdditionalFees ?? 0m;
            bill.TotalAmount = bill.AmountDue + bill.Penalty + additionalFees;
        }

        // ================================
        // 🔹 Step 3: Apply penalty to ALL unpaid bills
        // ================================
        public async Task ApplyPenaltyToAllUnpaidBillsAsync(string performedBy = "System")
        {
            var unpaidBills = await _context.Billings
                .Include(b => b.Consumer)
                .ThenInclude(c => c.User)
                .Where(b => b.Status == "Unpaid")
                .ToListAsync();

            if (!unpaidBills.Any()) return;

            foreach (var bill in unpaidBills)
            {
                decimal originalPenalty = bill.Penalty;
                decimal originalTotal = bill.TotalAmount;

                await ApplyPenaltyAsync(bill);
                bool isOverdue = bill.DueDate < DateTime.Today;

                // ✅ Only act when penalty or total amount actually changes
                if (bill.Penalty != originalPenalty || bill.TotalAmount != originalTotal)
                {
                    // 1️⃣ Log Audit Trail
                    _context.AuditTrails.Add(new AuditTrail
                    {
                        Action = "PenaltyUpdated",
                        PerformedBy = performedBy,
                        Timestamp = DateTime.Now,
                        Details = $"Penalty updated for BillNo {bill.BillNo} — from ₱{originalPenalty:N2} to ₱{bill.Penalty:N2} " +
                                  $"(Consumer: {bill.Consumer.FirstName} {bill.Consumer.LastName})"
                    });

                    // 2️⃣ In-App Notification (only when penalty or total changes)
                    var notif = new Notification
                    {
                        ConsumerId = bill.ConsumerId,
                        Title = isOverdue ? "💧 Overdue Water Bill" : "💧 Water Billing Update",
                        Message = isOverdue
                            ? $"Hello {bill.Consumer.FirstName}, your water bill (Bill No: {bill.BillNo}) dated {bill.BillingDate:yyyy-MM-dd} " +
                              $"is now **overdue** since {bill.DueDate:yyyy-MM-dd}. " +
                              $"Original Amount: ₱{bill.AmountDue:N2}, Penalty: ₱{bill.Penalty:N2}, Total Payable: ₱{bill.TotalAmount:N2}. " +
                              $"Please pay immediately to avoid disconnection."
                            : $"Hello {bill.Consumer.FirstName}, your bill (Bill No: {bill.BillNo}) has been updated. " +
                              $"New Total: ₱{bill.TotalAmount:N2}. Please settle before {bill.DueDate:yyyy-MM-dd}.",
                        CreatedAt = DateTime.Now
                    };

                    _context.Notifications.Add(notif);

                    // 3️⃣ Push Notification — only when something changed
                    await SendPushNotificationAsync(bill, notif, isOverdue);

                    // 4️⃣ Mark bill as modified for saving
                    _context.Entry(bill).State = EntityState.Modified;
                }
            }

            // Save only if actual changes occurred
            await _context.SaveChangesAsync();
        }


        // ================================
        // 🔹 Push Notification Sender
        // ================================
        private async Task SendPushNotificationAsync(Billing bill, Notification notif, bool isOverdue)
        {
            var user = bill.Consumer.User;
            if (user == null) return;

            var subscriptions = await _context.UserPushSubscriptions
                .Where(s => s.UserId == user.Id)
                .ToListAsync();

            if (!subscriptions.Any()) return;

            var vapidAuth = new VapidAuthentication(
                "BA_B1RL8wfVkIA7o9eZilYNt7D0_CbU5zsvqCZUFcCnVeqFr6a9BPxHPtWlNNgllEkEqk6jcRgp02ypGhGO3gZI",
                "0UqP8AfB9hFaQhm54rEabEwlaCo44X23BO6ID8n7E_U")
            {
                Subject = "mailto:cunanicolemichael@gmail.com"
            };

            var pushClient = new PushServiceClient
            {
                DefaultAuthentication = vapidAuth
            };

            string pushPayload = JsonSerializer.Serialize(new
            {
                title = notif.Title,
                body = isOverdue
                    ? $"Your water bill is overdue. Total payable: ₱{bill.TotalAmount:N2} (including ₱{bill.Penalty:N2} penalty)."
                    : $"New water bill available. Total due: ₱{bill.TotalAmount:N2}."
            });

            foreach (var sub in subscriptions)
            {
                var subscription = new PushSubscription
                {
                    Endpoint = sub.Endpoint,
                    Keys = new Dictionary<string, string>
            {
                { "p256dh", sub.P256DH },
                { "auth", sub.Auth }
            }
                };

                try
                {
                    await pushClient.RequestPushMessageDeliveryAsync(subscription, new PushMessage(pushPayload));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Push Error] {ex.Message}");
                }
            }
        }
    }
}
