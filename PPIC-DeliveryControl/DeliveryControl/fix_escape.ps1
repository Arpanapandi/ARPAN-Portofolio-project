$f = 'c:\DeliveryControl\DeliveryControl\Views\PreparationSchedule\Index.cshtml'
$raw = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)

# Cek apakah sudah ada di scope search (sebelum statusBadge)
if ($raw -match 'function escapeHtml' -and $raw -match 'function statusBadge') {
    # Cek posisi relatif - apakah escapeHtml sudah di atas statusBadge
    $idxEscape = $raw.IndexOf('function escapeHtml')
    $idxStatus = $raw.IndexOf('function statusBadge')
    if ($idxEscape -lt $idxStatus) {
        Write-Host "escapeHtml sudah ada SEBELUM statusBadge (index $idxEscape vs $idxStatus) - tidak perlu fix"
        exit 0
    } else {
        Write-Host "escapeHtml ada di index $idxEscape, statusBadge di index $idxStatus - perlu fix"
    }
}

$insertBefore = '                function statusBadge(status, prepStatus) {'

$insertCode = '                function escapeHtml(str) {
                    var div = document.createElement(''div'');
                    div.appendChild(document.createTextNode(str || ''''));
                    return div.innerHTML;
                }

                function statusBadge(status, prepStatus) {'

$count = ([regex]::Matches($raw, [regex]::Escape($insertBefore))).Count
Write-Host "Anchor 'function statusBadge' ditemukan: $count kali"

if ($count -eq 1) {
    $newContent = $raw.Replace($insertBefore, $insertCode)
    [System.IO.File]::WriteAllText($f, $newContent, [System.Text.Encoding]::UTF8)
    Write-Host "BERHASIL - escapeHtml ditambahkan sebelum statusBadge"
} elseif ($count -eq 0) {
    Write-Host "GAGAL - anchor tidak ditemukan"
} else {
    Write-Host "GAGAL - anchor ditemukan $count kali, tidak unik"
}
