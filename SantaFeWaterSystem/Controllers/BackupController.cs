using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class BackupController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public BackupController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // ================== Index ==================
        public IActionResult Index()
        {
            return View();
        }

        // ================== BackupDatabase ==================
        public async Task<IActionResult> BackupDatabase()
        {
            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Backups");
            if (!Directory.Exists(backupFolder))
                Directory.CreateDirectory(backupFolder);

            string backupFile = Path.Combine(backupFolder, $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql");

            var connStr = _configuration.GetConnectionString("DefaultConnection");
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connStr);

            try
            {
                var process = new ProcessStartInfo
                {
                    FileName = "pg_dump",
                    Arguments = $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -F c -b -v -f \"{backupFile}\" {builder.Database}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Environment["PGPASSWORD"] = builder.Password;

                using (var proc = Process.Start(process))
                {
                    string error = await proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    if (proc.ExitCode != 0)
                        throw new Exception(error);
                }

                _context.BackupLogs.Add(new BackupLog
                {
                    Action = "Backup",
                    FileName = Path.GetFileName(backupFile),
                    PerformedBy = User.Identity?.Name,
                    ActionDate = DateTime.Now
                });
                await _context.SaveChangesAsync();

                TempData["Success"] = "Backup created successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Backup failed: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        // ================== RestoreDatabase ==================
        [HttpGet]
        public IActionResult RestoreDatabase()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> RestoreDatabase(IFormFile backupFile)
        {
            if (backupFile == null || backupFile.Length == 0)
            {
                TempData["Error"] = "Please select a backup file.";
                return RedirectToAction("RestoreDatabase");
            }

            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Backups");
            if (!Directory.Exists(backupFolder))
                Directory.CreateDirectory(backupFolder);

            string filePath = Path.Combine(backupFolder, backupFile.FileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await backupFile.CopyToAsync(stream);
            }

            var connStr = _configuration.GetConnectionString("DefaultConnection");
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connStr);

            try
            {
                var process = new ProcessStartInfo
                {
                    FileName = "pg_restore",
                    Arguments = $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -d {builder.Database} -c \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Environment["PGPASSWORD"] = builder.Password;

                using (var proc = Process.Start(process))
                {
                    string error = await proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    if (proc.ExitCode != 0)
                        throw new Exception(error);
                }

                _context.BackupLogs.Add(new BackupLog
                {
                    Action = "Restore",
                    FileName = backupFile.FileName,
                    PerformedBy = User.Identity?.Name,
                    ActionDate = DateTime.Now
                });
                await _context.SaveChangesAsync();

                TempData["Success"] = "Database restored successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Restore failed: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        // ================== ManageBackups ==================
        public IActionResult ManageBackups()
        {
            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Backups");
            if (!Directory.Exists(backupFolder))
                Directory.CreateDirectory(backupFolder);

            var backups = Directory.GetFiles(backupFolder)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            return View(backups);
        }

        // ================== DownloadBackup ==================
        public IActionResult DownloadBackup(string fileName)
        {
            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Backups");
            string filePath = Path.Combine(backupFolder, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            return PhysicalFile(filePath, "application/octet-stream", fileName);
        }

        // ================== DeleteBackup ==================
        public IActionResult DeleteBackup(string fileName)
        {
            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Backups");
            string filePath = Path.Combine(backupFolder, fileName);

            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            TempData["Success"] = $"Backup {fileName} deleted.";
            return RedirectToAction("ManageBackups");
        }

        // ================== BackupHistory ==================
        public IActionResult BackupHistory()
        {
            var logs = _context.BackupLogs.OrderByDescending(b => b.ActionDate).ToList();
            return View(logs);
        }


        public async Task ScheduledBackup()
        {
            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Backups");
            if (!Directory.Exists(backupFolder))
                Directory.CreateDirectory(backupFolder);

            string backupFile = Path.Combine(backupFolder, $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
            string connectionString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

            try
            {
                using (var connection = new Npgsql.NpgsqlConnection(connectionString))
                {
                    string sql = $"BACKUP DATABASE \"{connection.Database}\" TO DISK='{backupFile}'";
                    using (var command = new Npgsql.NpgsqlCommand(sql, connection))
                    {
                        await connection.OpenAsync();
                        await command.ExecuteNonQueryAsync();
                    }
                }

                _context.BackupLogs.Add(new BackupLog
                {
                    Action = "Scheduled Backup",
                    FileName = Path.GetFileName(backupFile),
                    PerformedBy = "System",
                    ActionDate = DateTime.Now
                });

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Scheduled backup failed: " + ex.Message);
            }
        }

    }
}
