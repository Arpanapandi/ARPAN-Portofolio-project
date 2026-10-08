import re

with open('c:/DeliveryControl_backup_old/DeliveryControl/Views/Pulling/Index.cshtml', 'r', encoding='utf-8') as f:
    code = f.read()

# Fix 1: Restore state on server error
code = code.replace("#tagInput.val(payload.Tag);", "$('#tagInput').val(payload.Tag);")
code = code.replace("#labelInput.val('');", "$('#labelInput').val('');")

with open('c:/DeliveryControl_backup_old/DeliveryControl/Views/Pulling/Index.cshtml', 'w', encoding='utf-8') as f:
    f.write(code)
print("Done")
