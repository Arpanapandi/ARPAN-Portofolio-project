import zipfile, xml.etree.ElementTree as ET

excel_vins = set()

try:
    with zipfile.ZipFile('c:/DeliveryControl_backup_old/list item.xlsx') as z:
        strings = []
        if 'xl/sharedStrings.xml' in z.namelist():
            tree = ET.parse(z.open('xl/sharedStrings.xml'))
            for node in tree.iter():
                if node.tag.endswith('}t') and node.text:
                    strings.append(node.text.strip())

        sheet = ET.parse(z.open('xl/worksheets/sheet1.xml'))
        rows = sheet.findall('.//{http://schemas.openxmlformats.org/spreadsheetml/2006/main}row')
        
        for r in rows:
            vin_val = None
            status_val = None
            for c in r.findall('.//{http://schemas.openxmlformats.org/spreadsheetml/2006/main}c'):
                r_attr = c.attrib.get('r', '')
                v_node = c.find('.//{http://schemas.openxmlformats.org/spreadsheetml/2006/main}v')
                if v_node is not None:
                    v = v_node.text
                    if c.attrib.get('t') == 's':
                        v = strings[int(v)]
                    
                    if r_attr.startswith('I'):
                        vin_val = v
                    elif r_attr.startswith('G'):
                        status_val = v
            
            if status_val == 'Aktif' and vin_val:
                vin = vin_val.strip().upper()
                excel_vins.add(vin)
                
    print(f"Total Unique VINs in Excel: {len(excel_vins)}")
    with open('c:/DeliveryControl_backup_old/DeliveryControl/excel_vins.txt', 'w') as f:
        for v in excel_vins:
            f.write(f"{v}\n")
except Exception as e:
    print(f"Error: {e}")
