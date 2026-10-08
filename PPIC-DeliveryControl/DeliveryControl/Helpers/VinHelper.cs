using System;

namespace DeliveryControl.Helpers
{
    public static class VinHelper
    {
        public static string Normalize(string? vin)
        {
            if (string.IsNullOrEmpty(vin)) return string.Empty;

            string normalized = vin.Trim().ToUpper();

            // 1. Hapus suffix "X" (Visual only from form)
            if (normalized.EndsWith("X"))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            // 2. Hapus suffix "LB" (From import or internal)
            if (normalized.EndsWith("LB"))
            {
                normalized = normalized.Substring(0, normalized.Length - 2);
            }

            // 3. Hapus prefix "LB"
            if (normalized.StartsWith("LB"))
            {
                normalized = normalized.Substring(2);
            }

            return normalized;
        }

        public static bool IsMatch(string? vinA, string? vinB)
        {
            return Normalize(vinA) == Normalize(vinB);
        }

        /// <summary>
        /// Validasi apakah label mengandung kode VIN.
        /// Logika: label HARUS mengandung setidaknya prefix dari kode VIN (setelah normalisasi).
        /// Contoh: VIN "NA1580LBX" → normalized "NA1580" → label harus mengandung "NA1580"
        /// Atau VIN "NA1580LB" → normalized "NA1580" → label harus mengandung "NA1580"
        /// </summary>
        public static bool IsLabelContainsVin(string? label, string? vin)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(vin)) return false;

            string labelUpper = label.Trim().ToUpper();
            string vinUpper   = vin.Trim().ToUpper();

            // Check 1: Label mengandung VIN asli (raw) langsung
            if (labelUpper.Contains(vinUpper)) return true;

            // Check 2: Label mengandung VIN yang sudah di-normalize (tanpa suffix LBX / LB / X)
            string normalizedVin = Normalize(vin);
            if (!string.IsNullOrEmpty(normalizedVin) && labelUpper.Contains(normalizedVin)) return true;

            // Check 3: Label mengandung VIN tanpa suffix "X" saja
            string vinNoX = vinUpper.EndsWith("X") ? vinUpper.Substring(0, vinUpper.Length - 1) : vinUpper;
            if (!string.IsNullOrEmpty(vinNoX) && labelUpper.Contains(vinNoX)) return true;

            // Check 4: Label mengandung VIN tanpa suffix "LB"
            string vinNoLb = vinUpper.EndsWith("LB") ? vinUpper.Substring(0, vinUpper.Length - 2) : vinUpper;
            if (!string.IsNullOrEmpty(vinNoLb) && labelUpper.Contains(vinNoLb)) return true;

            // Check 5: Relaxed matching for MS series (e.g. MS0020 allows MS002 in label)
            if (normalizedVin.StartsWith("MS") && normalizedVin.Length >= 5)
            {
                string shortVin = normalizedVin.Substring(0, 5);
                if (labelUpper.Contains(shortVin)) return true;
            }

            return false;
        }

        public static bool IsKanbanBelongsToManifest(string? kanban, string? manifest)
        {
            if (string.IsNullOrEmpty(kanban) || string.IsNullOrEmpty(manifest)) return true;
            
            var kbn = kanban.Trim().ToUpper();
            var manifestUpper = manifest.Trim().ToUpper();
            
            if (kbn.StartsWith("KBN"))
            {
                if (manifestUpper.StartsWith("KBN"))
                {
                    return kbn.StartsWith(manifestUpper);
                }
                if (kbn.Length >= 3 + manifestUpper.Length)
                {
                    var manifestInKbn = kbn.Substring(3, manifestUpper.Length);
                    return manifestInKbn == manifestUpper;
                }
                return false;
            }
            
            if (kbn.StartsWith("DN"))
            {
                // Format DN hanya mencocokkan manifest jika manifest juga berformat DN (misal ADM KAP / SAP: DN51..., DN41...)
                if (manifestUpper.StartsWith("DN"))
                {
                    if (kbn.Length >= manifestUpper.Length)
                    {
                        var manifestInKbn = kbn.Substring(0, manifestUpper.Length);
                        return manifestInKbn == manifestUpper;
                    }
                    return false;
                }
                // Jika manifest bukan berawalan DN (misal ADM KEP dengan nomor PO/order 4020...),
                // maka barcode DN pada kanban tidak meng-encode nomor manifest di depannya.
                return true;
            }
            
            return true;
        }

        public static string? ExtractManifestFromKanban(string? kanban, string? currentManifest)
        {
            if (string.IsNullOrEmpty(kanban)) return null;
            var kbn = kanban.Trim().ToUpper();
            int currentLength = currentManifest?.Trim().Length ?? 10;
            
            if (kbn.StartsWith("DN"))
            {
                if (currentManifest != null && currentManifest.Trim().ToUpper().StartsWith("DN"))
                {
                    if (kbn.Length >= currentLength)
                        return kbn.Substring(0, currentLength);
                }
            }
            else if (kbn.StartsWith("KBN"))
            {
                if (kbn.Length >= 3 + currentLength)
                    return kbn.Substring(3, currentLength);
            }
            return null;
        }

        public static System.Collections.Generic.List<string> GetItemTags(string? vin, string? itemCode, string? partNo)
        {
            var tags = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(vin))
            {
                tags.Add(vin);
                var norm = Normalize(vin);
                if (!string.IsNullOrEmpty(norm)) tags.Add(norm);
            }
            if (!string.IsNullOrEmpty(itemCode))
            {
                tags.Add(itemCode);
                var norm = Normalize(itemCode);
                if (!string.IsNullOrEmpty(norm)) tags.Add(norm);
            }
            if (!string.IsNullOrEmpty(partNo))
            {
                tags.Add(partNo);
                var norm = Normalize(partNo);
                if (!string.IsNullOrEmpty(norm)) tags.Add(norm);
            }
            
            var result = new System.Collections.Generic.List<string>();
            foreach (var t in tags)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    var u = t.Trim().ToUpper();
                    if (!result.Contains(u)) result.Add(u);
                }
            }
            return result;
        }

        public static string FormatLabel(string? label, string? vin)
        {
            if (string.IsNullOrEmpty(label) || !label.ToUpper().Contains("LB"))
            {
                return (string.IsNullOrEmpty(vin) ? (label ?? "") : vin) + "LB";
            }
            return label;
        }
    }
}
