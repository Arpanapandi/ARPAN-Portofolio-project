namespace DeliveryControl.Services
{
    /// <summary>
    /// Singleton yang menyimpan versi cache GetStockViewModel.
    /// Setiap kali ada perubahan data stok (ManualAdjust, HideRack, dll.),
    /// Invalidate() dipanggil → versi bertambah → semua cache entry lama tidak dipakai lagi.
    /// </summary>
    public class StockCacheService
    {
        private volatile int _version = 0;
        public int Version => _version;
        public void Invalidate() => System.Threading.Interlocked.Increment(ref _version);
    }
}
