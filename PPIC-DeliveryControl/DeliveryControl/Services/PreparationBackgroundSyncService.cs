using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Background service yang otomatis menjalankan sinkronisasi data pending setiap 5 menit.
    /// Ini memastikan item yang di-scan lebih awal otomatis masuk ke jadwal saat waktu Start Prep tiba.
    /// </summary>
    public class PreparationBackgroundSyncService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PreparationBackgroundSyncService> _logger;

        public PreparationBackgroundSyncService(
            IServiceScopeFactory scopeFactory,
            ILogger<PreparationBackgroundSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PreparationBackgroundSyncService started. Running every 5 minutes.");

            // Jalankan satu kali saat startup (tanpa delay), lalu lanjut loop 5 menit.
            // Ini memastikan pending records yang ada saat app restart langsung diproses
            // tanpa harus menunggu 5 menit pertama.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var syncService = scope.ServiceProvider.GetRequiredService<PreparationSyncService>();
                        
                        _logger.LogInformation("Running background sync for pending records...");
                        int syncedCount = await syncService.SyncAllPendingAsync(DateTime.Today);
                        
                        if (syncedCount > 0)
                        {
                            _logger.LogInformation("Background sync completed. {Count} records synced.", syncedCount);
                        }

                        // Tandai jadwal yang sudah lewat due time sebagai "Delayed" (setiap siklus)
                        await syncService.AutoMarkDelayedAsync();

                        // Startup recalc DIHAPUS: RecalcActualQtyForDateRangeAsync dapat menurunkan
                        // nilai KBN PREP saat app restart akibat DN cross-manifest filter yang berbeda
                        // dengan logika Save action. Koreksi manual via tombol "Recalc KBN" di dashboard.
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during background preparation sync.");
                }

                // Wait for 5 minutes
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
