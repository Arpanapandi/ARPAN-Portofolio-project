$path = "c:\DeliveryControl\DeliveryControl\Views\PreparationWorkflow\Index.cshtml"
$content = Get-Content $path -Raw
$old = "\$('#idStatusSection')\.slideDown\(\);\s+\$('#idStatusText')\.html\(`Item: \${res\.item\.itemName} <br><small class='text-warning fw-bold'>STOCK: \${res\.item\.currentStock \|\| 0} BOX</small>`\);"
$new = @"
                                 $('#idStatusSection').slideDown();
                                 if (isTwoPointCheck) {
                                     $('#idStatusText').html(`<i class="bi bi-shield-check-fill text-info me-1"></i> <span class="text-info fw-bold">TWO-POINT CHECK AKTIF</span><br><small class="text-white-50">Cukup scan Rak & Label (Otomatis skip Kanban).</small>`);
                                     $('#kanbanInput').parent().parent().hide();
                                     $('#kanbanInput').val('TWO-POINT-CHECK');
                                 } else {
                                     $('#idStatusText').html(``Item: \${res.item.itemName} <br><small class='text-warning fw-bold'>STOCK: \${res.item.currentStock || 0} BOX</small>``);
                                     $('#kanbanInput').parent().parent().show();
                                 }
"@
# Gunakan regex untuk mengganti
$content = $content -replace $old, $new
Set-Content $path $content
