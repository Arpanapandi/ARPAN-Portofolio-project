
        $(document).ready(function () {
            // Toast Helper Function
            function showScannerToast(message, type = 'primary') {
                const $toast = $('#scannerToast');
                if (!$toast.length) return;
                $toast.removeClass('bg-primary bg-success bg-danger bg-warning');
                $toast.addClass('bg-' + type);
                $('#scannerToastBody').text(message);
                const toast = new bootstrap.Toast($toast[0], { delay: 3000 });
                toast.show();
            }

            let activeInputId = null;

            // Modal handling
            const scannerModalElement = document.getElementById('scannerModal');
            const scannerModal = new bootstrap.Modal(scannerModalElement);
            
            // Scanner implementation using Html5Qrcode
            let html5QrCode = null;

            $(scannerModalElement).on('shown.bs.modal', function () {
                initScanner();
            });

            $(scannerModalElement).on('hidden.bs.modal', function () {
                stopScanner();
            });

            $('.btn-scan-trigger').click(function() {
                activeInputId = $(this).data('target');
                const fieldName = (activeInputId || "").replace('Input', '').toUpperCase();
                $('#currentScanningField').text("Scanning " + fieldName);
                scannerModal.show();
            });

            $('#btnRestartCamera').click(function(e) {
                e.preventDefault();
                stopScanner().then(() => initScanner());
            });

            function initScanner() {
                if (!window.isSecureContext) {
                    $('#scanner-status').html('<span class="text-danger">Error: HTTPS diperlukan</span>');
                    return;
                }

                const config = { 
                    fps: 25, 
                    aspectRatio: 1.0,
                    qrbox: { width: 250, height: 250 }, // Square for better target match
                    showTorchButtonIfSupported: true,
                    videoConstraints: {
                        facingMode: "environment",
                        focusMode: "continuous", // Try to force autofocus
                        width: { min: 640, ideal: 1280, max: 1920 },
                        height: { min: 480, ideal: 720, max: 1080 }
                    }
                };

                if (!html5QrCode) {
                    html5QrCode = new Html5Qrcode("reader");
                }

                $('#scanner-status').text("Mengakses Kamera...");
                
                html5QrCode.start(
                    { facingMode: "environment" }, 
                    config,
                    (decodedText) => onDetected(decodedText),
                    (errorMessage) => {}
                ).then(() => {
                    $('#scanner-status').text("Scanner Aktif");
                }).catch(err => {
                    $('#scanner-status').text("Gagal: " + err);
                });
            }

            function onDetected(code) {
                if (activeInputId) {
                    const currentId = activeInputId;
                    const trimmedCode = code.trim(); // Keep original case for now, validation will handle upper

                    // Apply validation and processing based on target input
                    if (currentId === 'tagInput') {
                        // Validation for tagInput (VIN) must have 'X' suffix
                        if (!trimmedCode.toUpperCase().endsWith('X')) {
                            showScannerToast("KODE TIDAK VALID: Wajib scan dari Label asli (Missing X)", "danger");
                            $(`#${currentId}`).val('');
                            scannerModal.hide();
                            return;
                        }
                        // Strip the 'X' for internal processing
                        const cleanCode = trimmedCode.substring(0, trimmedCode.length - 1);
                        $(`#${currentId}`).val(cleanCode).addClass('active-scan');
                        setTimeout(() => $(`#${currentId}`).removeClass('active-scan'), 500);
                        scannerModal.hide();
                        lookupItem(cleanCode);
                    } else if (currentId === 'labelInput') {
                        // Validation for labelInput must NOT have 'X' suffix
                        if (trimmedCode.toUpperCase().endsWith('X')) {
                            showScannerToast("KODE TIDAK VALID: Label tidak boleh mengandung suffix X", "danger");
                            $(`#${currentId}`).val('');
                            scannerModal.hide();
                            return;
                        }
                        $(`#${currentId}`).val(trimmedCode).addClass('active-scan');
                        setTimeout(() => $(`#${currentId}`).removeClass('active-scan'), 500);
                        scannerModal.hide();
                        // AUTOMATION: Auto Submit after Label Scan
                        setTimeout(() => {
                           triggerAutoSave();
                        }, 300);
                    }
                }
            }

            async function stopScanner() {
                if (html5QrCode && html5QrCode.isScanning) {
                    try {
                        await html5QrCode.stop();
                    } catch (err) {
                        console.warn("Stop Error:", err);
                    }
                }
            }

            // --- GLOBAL SCANNER HANDLER (ANTI-MANUAL & READONLY SUPPORT) ---
            let scanBuffer = "";
            let lastKeyTime = Date.now();
            const SCAN_INTERVAL = 50; // ms (Scanner is very fast, Human is slow)

            window.addEventListener('keydown', function (e) {
                const currentTime = Date.now();
                
                // If focus is on a normal interactive element (like a modal or another input), don't intercept
                if (e.target.tagName === "INPUT" && !$(e.target).hasClass('cyber-field')) return;
                if (e.target.tagName === "TEXTAREA" || e.target.tagName === "SELECT") return;
                if ($('.modal.show').length > 0) return;

                // Check speed: If too slow, it's likely a human typing. Reset buffer.
                if (currentTime - lastKeyTime > SCAN_INTERVAL) {
                    scanBuffer = "";
                }
                lastKeyTime = currentTime;

                // If Enter key is hit (End of Scan)
                if (e.keyCode === 13) {
                    if (scanBuffer.length > 0) {
                        processScanResult(scanBuffer);
                        scanBuffer = "";
                    }
                    e.preventDefault();
                    return;
                }

                // Collect only visible characters (avoid Shift/Ctrl/etc)
                if (e.key.length === 1) {
                    scanBuffer += e.key;
                }
            });

            function processScanResult(code) {
                code = code.trim();
                const codeUpper = code.toUpperCase();

                // Determine target: If Tag is empty, it's a Tag Scan. Else it's a Label Scan.
                if (!$('#tagInput').val()) {
                    // --- VIN VALIDATION (Must have X) ---
                    if (!codeUpper.endsWith('X')) {
                        showScannerToast("KODE TIDAK VALID: Wajib scan dari Label asli (Missing X)", "danger");
                        return;
                    }
                    const cleanVin = code.substring(0, code.length - 1);
                    $('#tagInput').val(cleanVin).addClass('active-scan');
                    setTimeout(() => $('#tagInput').removeClass('active-scan'), 500);
                    lookupItem(cleanVin);
                } else {
                    // --- LABEL VALIDATION (Must NOT have X) ---
                    if (codeUpper.endsWith('X')) {
                        showScannerToast("KODE TIDAK VALID: Label tidak boleh mengandung suffix X", "danger");
                        return;
                    }
                    $('#labelInput').val(code).addClass('active-scan');
                    setTimeout(() => $('#labelInput').removeClass('active-scan'), 500);
                    triggerAutoSave();
                }
            }

            // --- ITEM LOOKUP LOGIC ---
            let isItemValid = false;

            function lookupItem(tag) {
                if (!tag) return;

                $.ajax({
                    url: '/Pulling/GetItemInfo',
                    type: 'GET',
                    data: { tag: tag },
                    success: function(response) {
                        if (response.success) {
                            isItemValid = true;
                            enableLabelInput();
                            
                            // Workflow: Automatically focus Label for External Scanner
                            setTimeout(() => {
                                $('#labelInput').focus();
                            }, 300);
                        } else {
                            isItemValid = false;
                            disableLabelInput();
                            showScannerToast("ERROR: " + (response.message || "Item tidak ditemukan."), "danger");
                            $('#tagInput').val(''); // --- AUTO RESET ---
                        }
                    },
                    error: function() {
                        showScannerToast("Gagal menghubungi server untuk lookup item.", "danger");
                        $('#tagInput').val(''); // --- AUTO RESET ---
                    }
                });
            }

            function showPartDetails(data) {
                $('#valLocation').text(data.rack + "." + data.noRack); // Lokasi Rack derived from Rack.NoRack
                $('#valPlant').text(data.plant);
                $('#valRackInfo').text(data.rack);
                $('#valCustomer').text(data.customer);
                $('#valCategory').text(data.category);
                $('#valVinQpc').text(data.vin + " / " + data.qtyLot);
                $('#valLimits').text(data.rackMin + " / " + data.rop + " / " + data.rackMax);
                $('#valCurrentStock').text(data.currentStock + " BOX");

                // Badge Status
                const $badge = $('#badgeStatus');
                $badge.removeClass('bg-success bg-warning bg-danger').text('Status: ' + data.status.toUpperCase());
                if (data.status === 'Normal') $badge.addClass('bg-success');
                else if (data.status === 'Shortage') $badge.addClass('bg-danger');
                else $badge.addClass('bg-warning text-dark');

                $('#partDetailContainer').slideDown();
            }

            function hidePartDetails() {
                $('#partDetailContainer').slideUp();
            }

            function enableLabelInput() {
                $('#labelInput, .btn-scan-trigger[data-target="labelInput"]').prop('disabled', false);
                $('#labelHint').fadeIn();
                $('#labelInput').focus();
            }

            function disableLabelInput() {
                $('#labelInput, .btn-scan-trigger[data-target="labelInput"]').prop('disabled', true);
                $('#labelHint').fadeOut();
                $('#labelInput').val('');
            }

            // Removed: $('#tagInput').on('input', function() { ... }); // Global handler takes over
            // Removed: $('#labelInput').on('change keypress', function(e) { ... }); // Global handler takes over

            function triggerAutoSave() {
                 if ($('#btnSavePulling').prop('disabled')) return;
                 $('#btnSavePulling').click();
            }

            // Save Data (AJAX)
            $('#btnSavePulling').click(function () {
                const tag = $('#tagInput').val();
                const label = $('#labelInput').val();

                if (!tag || !label) {
                    // alert('Lengkapi data Tag dan Label!'); // Suppress silent errors during typing
                    if (!tag) $('#tagInput').focus();
                    else if (!label) $('#labelInput').focus();
                    return;
                }

                const data = {
                    Tag: tag.trim(),
                    Label: label.trim(),
                    Quantity: 1
                };

                const $btn = $(this);
                const $indicator = $('#autoSaveIndicator');
                
                $btn.prop('disabled', true);
                $indicator.show(); // Show visual feedback

                $.ajax({
                    url: '/Pulling/Save',
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify(data),
                    success: function (response) {
                        if (response.success) {
                            showScannerToast('SUKSES: ' + response.message, 'success');
                            $('#tagInput, #labelInput').val('');
                            hidePartDetails();
                            disableLabelInput();
                            $('#tagInput').focus(); // Ready for next scan
                        } else {
                            showScannerToast('GAGAL: ' + response.message, 'danger');
                            $('#labelInput').val('').focus();
                        }
                    },
                    error: function () {
                        showScannerToast('Terjadi kesalahan koneksi.', 'danger');
                    },
                    complete: function() {
                        $btn.prop('disabled', false);
                         $indicator.hide();
                    }
                });
            });

            // Hardware Scanner Support (Legacy - keeping for safety but global is primary)
            $('#tagInput, #labelInput').on('keypress', function(e) {
                if (e.which == 13) e.preventDefault();
            });

            // Auto-focus Tag on load
            $('#tagInput').focus();

            // Removed: Global click to refocus for external scanners

            // Visual feedback for focus
            $('input').on('focus', function() {
                $(this).closest('.custom-cyber-input-group').addClass('selected-glow');
            }).on('blur', function() {
                $(this).closest('.custom-cyber-input-group').removeClass('selected-glow');
            });
        });
