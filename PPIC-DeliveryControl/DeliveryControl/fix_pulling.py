import re

with open('c:/DeliveryControl_backup_old/DeliveryControl/Views/Pulling/Index.cshtml', 'r', encoding='utf-8') as f:
    code = f.read()

# Fix 1: Restore state on server error
pattern_1 = r"(\s*logNGScan\('Pulling', payload\.Tag \|\| '', payload\.Label \|\| '', '', data\.message \|\| 'Scan gagal'\);\s*)(return;\s*\})"
replacement_1 = r'''\1
                            // [FIX] Restore state agar user bisa langsung scan ulang label tanpa scan VIN lagi
                            if (payload.Tag) {
                                isItemValid = true;
                                lockedVin = payload.Tag;
                                #tagInput.val(payload.Tag);
                                enableLabelInput();
                                #labelInput.val('');
                            }
                            \2'''

code, count_1 = re.subn(pattern_1, replacement_1, code, flags=re.DOTALL)
print(f"Replaced target 1: {count_1}")

# Fix 2: Focus correct input on modal close
pattern_2 = r"(\s*\$\('#btnCloseScanError'\)\.on\('click', function\(\) \{\s*\$\(this\)\.blur\(\);\s*scanErrorModal\.hide\(\);\s*\}\);)"
replacement_2 = r'''
            #btnCloseScanError.on('click', function() {
                .blur(); // cegah aria-hidden warning
                scanErrorModal.hide();
                
                // [FIX] Fokus kembali ke input yang benar
                setTimeout(() => {
                    if (isItemValid) {
                        #labelInput.focus();
                    } else {
                        #tagInput.focus();
                    }
                }, 300);
            });'''

code, count_2 = re.subn(pattern_2, replacement_2, code, flags=re.DOTALL)
print(f"Replaced target 2: {count_2}")

with open('c:/DeliveryControl_backup_old/DeliveryControl/Views/Pulling/Index.cshtml', 'w', encoding='utf-8') as f:
    f.write(code)
print("Done")
