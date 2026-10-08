
        $(document).ready(function () {
            let html5QrCode = null;
            const scannerModalEl = document.getElementById('scannerModal');
            const scannerModal = new bootstrap.Modal(scannerModalEl);
            let activeInputId = null;
            let currentScheduleId = null;
            let isSubmitting = false; // Flag to prevent duplicate requests

            // --- GLOBAL SCANNER HANDLER (ANTI-MANUAL & READONLY SUPPORT) ---
            let scanBuffer = "";
            let lastKeyTime = Date.now();
            const SCAN_INTERVAL = 50; 

            window.addEventListener('keydown', function (e) {
                const currentTime = Date.now();
                
                // Ignore keydown events if the target is not a cyber-field input, or is a textarea/select
                if (e.target.tagName === "INPUT" && !$(e.target).hasClass('cyber-field')) return;
                if (e.target.tagName === "TEXTAREA" || e.target.tagName === "SELECT") return;
                // Ignore keydown events if any modal is currently open
                if ($('.modal.show').length > 0) return;

                if (currentTime - lastKeyTime > SCAN_INTERVAL) {
                    scanBuffer = "";
                }
                lastKeyTime = currentTime;

                if (e.keyCode === 13) { // Enter key
                    if (scanBuffer.length > 0) {
                        processScanResult(scanBuffer);
                        scanBuffer = "";
                    }
                    e.preventDefault(); // Prevent form submission or other default behavior
                    return;
                }

                // Only append printable characters
                if (e.key.length === 1) {
                    scanBuffer += e.key;
                }
            });

            function processScanResult(code) {
                code = code.trim();
                const codeUpper = code.toUpperCase();
                const tag = $('#tagInput').val().trim();
                const label = $('#labelInput').val().trim();
                const kanban = $('#kanbanInput').val().trim();

                // Target logic: VIN -> Label -> Kanban
                if (!tag) {
                    // --- VIN VALIDATION (Must have X) ---
                    if (!codeUpper.endsWith('X')) {
                        showScannerToast("KODE TIDAK VALID: Wajib scan dari Label asli (Missing X)", "danger");
                        return;
                    }
                    const cleanVin = code.substring(0, code.length - 1);
                    $('#tagInput').val(cleanVin).addClass('active-scan');
                    setTimeout(() => $('#tagInput').removeClass('active-scan'), 500);
                    performLookup();
                } else if (!label) {
                    // --- LABEL VALIDATION (Must NOT have X) ---
                    if (codeUpper.endsWith('X')) {
                        showScannerToast("KODE TIDAK VALID: Label tidak boleh mengandung suffix X", "danger");
                        return;
                    }
                    $('#labelInput').val(code).addClass('active-scan');
                    setTimeout(() => $('#labelInput').removeClass('active-scan'), 500);
                    performLookup();
                } else if (!kanban) {
                    // --- KANBAN VALIDATION (Must NOT have X) ---
                    if (codeUpper.endsWith('X')) {
                        showScannerToast("KODE TIDAK VALID: Kanban tidak boleh mengandung suffix X", "danger");
                        return;
                    }
                    $('#kanbanInput').val(code).addClass('active-scan');
                    setTimeout(() => $('#kanbanInput').removeClass('active-scan'), 500);
                    checkAndAdvance();
                }
            }

            // --- SCANNER HANDLERS ---
            $('.btn-scan-trigger').click(function() {
                activeInputId = $(this).data('target');
                $('#scannerModalLabel').text("MEMINDAI: " + (activeInputId || "ITEM").toUpperCase());
                scannerModal.show();
            });

            $(scannerModalEl).on('shown.bs.modal', function () {
                initScanner();
            });

            $(scannerModalEl).on('hidden.bs.modal', function () {
                stopScanner();
                // Ensure focus is definitely not on the modal trigger if possible, though we handled it in onDetected
            });

            async function initScanner() {
                try {
                    const config = { 
                        fps: 25, 
                        aspectRatio: 1.0, 
                        qrbox: { width: 250, height: 250 },
                        showTorchButtonIfSupported: true,
                        videoConstraints: {
                            facingMode: "environment",
                            focusMode: "continuous",
                            width: { min: 640, ideal: 1280, max: 1920 },
                            height: { min: 480, ideal: 720, max: 1080 }
                        }
                    };
                    
                    if (!html5QrCode) html5QrCode = new Html5Qrcode("reader");
                    if (html5QrCode.isScanning) await html5QrCode.stop();

                    $('#scanner-status').text("Mengakses Kamera...");
                    await html5QrCode.start({ facingMode: "environment" }, config, onDetected);
                    $('#scanner-status').text("Scanner Aktif");
                } catch (err) {
                    $('#scanner-status').text("Gagal: " + err);
                    try {
                         if (html5QrCode) await html5QrCode.start({ facingMode: "user" }, { fps: 20, qrbox: { width: 250, height: 250 } }, onDetected);
                    } catch (e2) {
                         showScannerToast("Tidak dapat mengakses kamera: " + e2, "danger");
                    }
                }
            }

            function onDetected(decodedText) {
                // Validation for tagInput (VIN) must have 'X' suffix
                if (activeInputId === 'tagInput') {
                    if (!decodedText.trim().toUpperCase().endsWith('X')) {
                        showScannerToast("KODE TIDAK VALID: Wajib scan dari Label asli (Missing X)", "danger");
                        $('#tagInput').val('');
                        scannerModal.hide();
                        return;
                    }
                    // Strip 'X'
                    decodedText = decodedText.trim().substring(0, decodedText.trim().length - 1);
                }
                
                // Validation for Label/Kanban must NOT have 'X' suffix
                if (activeInputId === 'labelInput' || activeInputId === 'kanbanInput') {
                    if (decodedText.trim().toUpperCase().endsWith('X')) {
                        showScannerToast("KODE TIDAK VALID: Label/Kanban tidak boleh mengandung suffix X", "danger");
                        $(`#${activeInputId}`).val('');
                        scannerModal.hide();
                        return;
                    }
                }

                // FIX ARIA HIDDEN: Focus OUT of the modal immediately.
                if (activeInputId) {
                    const $input = $(`#${activeInputId}`);
                    $input.val(decodedText).addClass('active-scan');
                    $input.prop('disabled', false).focus(); // Ensure it's enabled before focusing
                } else {
                    $('#tagInput').focus();
                }
                
                // Delay hiding to let the focus event propagate
                setTimeout(() => {
                    scannerModal.hide();
                    performLookup();
                }, 150);
            }

            async function stopScanner() {
                if (html5QrCode && html5QrCode.isScanning) await html5QrCode.stop();
            }

            // --- INPUT LOGIC ---

            function checkAndAdvance() {
                const tag = $('#tagInput').val().trim();
                const label = $('#labelInput').val().trim();
                const kanban = $('#kanbanInput').val().trim();

                if (!tag) { $('#tagInput').focus(); return; }
                if (!label && !$('#labelInput').prop('disabled')) { $('#labelInput').focus(); return; }
                if (!kanban && !$('#kanbanInput').prop('disabled')) { $('#kanbanInput').focus(); return; }

                if (tag && label && kanban) {
                    triggerPrepAutoSave();
                }
            }

            function triggerPrepAutoSave() {
                if (isSubmitting || $('#btnSavePreparation').prop('disabled')) return;
                $('#btnSavePreparation').click();
            }

            $('#tagInput, #labelInput, #kanbanInput').on('change keypress', function(e) {
                // Prevent duplicate trigger if Enter is pressed (Internal Scanner)
                if(e.type === 'change' && $(this).data('last-event') === 'keypress') {
                    $(this).data('last-event', '');
                    return;
                }
                if(e.type === 'keypress') {
                    $(this).data('last-event', 'keypress');
                    if(e.which !== 13) return;
                }
                
                const id = $(this).attr('id');
                if (id === 'kanbanInput') {
                    checkAndAdvance();
                } else {
                    performLookup();
                }
            });

            function resetInputs() {
                const isFastMode = $('#fastScanMode').is(':checked');
                $('#idStatusSection').slideUp();
                $('#tagInput, #labelInput, #kanbanInput').val('').removeClass('active-scan');
                
                if (!isFastMode) {
                    $('#labelInput, #kanbanInput, .btn-scan-trigger[data-target="labelInput"], .btn-scan-trigger[data-target="kanbanInput"]').prop('disabled', true);
                } else {
                    $('#labelInput, #kanbanInput, .btn-scan-trigger').prop('disabled', false);
                }
                
                currentScheduleId = null;
                $('#tagInput').focus();
            }

            function performLookup() {
                const isFastMode = $('#fastScanMode').is(':checked');
                let tag = $('#tagInput').val().trim();
                const label = $('#labelInput').val().trim();
                const kanban = $('#kanbanInput').val().trim();

                if (!tag) return;

                // Validation for External Scanner / Manual Bypass: Must end with 'X'
                // If it's tagInput and tag is not empty, check if it has X
                if (tag && !label && !kanban) {
                    if (!tag.toUpperCase().endsWith('X') && tag.length > 5) {
                         showScannerToast("KODE TIDAK VALID: Wajib scan dari Label asli (Missing X)", "danger");
                         $('#tagInput').val('').focus();
                         return;
                    }
                    if (tag.toUpperCase().endsWith('X')) {
                        tag = tag.substring(0, tag.length - 1);
                        $('#tagInput').val(tag);
                    }
                }
                
                // Validation for Label/Kanban: Must NOT end with 'X'
                if (label && label.toUpperCase().endsWith('X')) {
                    showScannerToast("KODE TIDAK VALID: Label tidak boleh mengandung suffix X", "danger");
                    $('#labelInput').val('').focus();
                    return;
                }
                if (kanban && kanban.toUpperCase().endsWith('X')) {
                    showScannerToast("KODE TIDAK VALID: Kanban tidak boleh mengandung suffix X", "danger");
                    $('#kanbanInput').val('').focus();
                    return;
                }

                $.ajax({
                    url: '/PreparationWorkflow/LookupSchedule',
                    type: 'GET',
                    data: { tag: tag, label: label, kanban: kanban },
                    success: function(res) {
                        if (res.success) {
                            if (res.found) {
                                currentScheduleId = res.schedule.scheduleId;
                                $('#idStatusSection').slideDown();
                                $('#idStatusText').html(`Ditemukan: ${res.schedule.customerName} <br><small class='text-warning fw-bold'>STOCK: ${res.item.currentStock || 0} BOX</small>`);
                                $('#idTargetInfo').text(res.schedule.scheduleNumber);
                                
                                // Enable All
                                $('#labelInput, #kanbanInput, .btn-scan-trigger').prop('disabled', false);

                                // Auto Advance: Focus next field for External Scanner
                                const delay = isFastMode ? 50 : 300;
                                if (tag && label && kanban) {
                                    triggerPrepAutoSave();
                                } else if (tag && label) {
                                    setTimeout(() => $('#kanbanInput').focus(), delay);
                                } else if (tag) {
                                    setTimeout(() => $('#labelInput').focus(), delay);
                                }

                            } else if (res.item) {
                                // Partial Match / Step
                                
                                // Handle Specific Validation Failures (Label/Kanban mismatch)
                                if (res.message && (res.message.includes("tidak sesuai") || res.message.includes("tidak cocok") || res.message.includes("Label tidak sesuai"))) {
                                    showScannerToast(res.message, "danger");
                                    if (res.step === 'label') {
                                        $('#labelInput').val(''); // --- AUTO RESET ---
                                    } else if (res.step === 'kanban') {
                                        $('#kanbanInput').val(''); // --- AUTO RESET ---
                                    }
                                    return; 
                                }

                                $('#idStatusSection').slideDown();
                                $('#idStatusText').html(`Item: ${res.item.itemName} <br><small class='text-warning fw-bold'>STOCK: ${res.item.currentStock || 0} BOX</small>`);
                                $('#idTargetInfo').text(res.item.vin);
                                
                                // Always enable Label if Tag is valid
                                $('#labelInput, .btn-scan-trigger[data-target="labelInput"]').prop('disabled', false);

                                // Focus next field for External Scanner
                                const delay = isFastMode ? 50 : 300;
                                if (label) {
                                    $('#kanbanInput, .btn-scan-trigger[data-target="kanbanInput"]').prop('disabled', false);
                                    setTimeout(() => $('#kanbanInput').focus(), delay);
                                } else {
                                    setTimeout(() => $('#labelInput').focus(), delay);
                                }
                            }
                        } else {
                            if (res.message) showScannerToast(res.message, "danger");
                            resetInputs();
                            $('#tagInput').val(''); // --- AUTO RESET ---
                        }
                    },
                    error: function() {
                         showScannerToast("Gagal menghubungi server.", "danger");
                         resetInputs();
                         $('#tagInput').val(''); // --- AUTO RESET ---
                    }
                });
            }

            $('#btnSavePreparation').click(function () {
                if (isSubmitting) return;

                const data = {
                    Tag: $('#tagInput').val().trim(),
                    Label: $('#labelInput').val().trim(),
                    Kanban: $('#kanbanInput').val().trim()
                };

                if (!data.Tag || !data.Label || !data.Kanban) return; 

                isSubmitting = true;
                const $btn = $(this);
                $btn.prop('disabled', true);
                $('#prepLoadingIndicator').show();

                $.ajax({
                    url: '/PreparationWorkflow/Save',
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify(data),
                    success: function (res) {
                        if (res.success) {
                            showScannerToast(res.message, "success");
                            // Update progress detail if needed, but for effectiveness, we reset and stay on page
                            if (res.scheduleDetails) {
                                $('#progressTargetName').text(res.scheduleDetails.customerName);
                                $('#progressPercentTotal').text(Math.round(res.scheduleDetails.totalPercent) + '%');
                                $('#progressDetailArea').fadeIn();
                                setTimeout(() => $('#progressDetailArea').fadeOut(), 3000);
                            }

                            isSubmitting = false;
                            $btn.prop('disabled', false);
                            $('#prepLoadingIndicator').hide();
                            resetInputs();
                            
                            // If it was a critical message (FIFO), maybe we still want to reload after a bit
                            if (res.isFallback || (res.message && res.message.includes("FIFO"))) {
                                setTimeout(() => location.reload(), 2000);
                            }
                        } else {
                            showScannerToast(res.message, "danger");
                            isSubmitting = false;
                            $btn.prop('disabled', false);
                            $('#prepLoadingIndicator').hide();
                            
                            // IF QTY FULL: Reset to VIN as per user request
                            if (res.message && res.message.includes("CUKUP")) {
                                resetInputs();
                            } else {
                                $('#kanbanInput').val('').focus();
                            }
                        }
                    },
                    error: function() {
                         showScannerToast("Connection Error", "danger");
                         isSubmitting = false; // Reset on error
                         $btn.prop('disabled', false);
                         $('#prepLoadingIndicator').hide();
                    }
                });
            });

            // Toast Helper Function
            function showScannerToast(message, type = 'primary') {
                const $toast = $('#scannerToast');
                $toast.removeClass('bg-primary bg-success bg-danger bg-warning');
                $toast.addClass('bg-' + type);
                $('#scannerToastBody').text(message);
                const toast = new bootstrap.Toast($toast[0], { delay: 3000 });
                toast.show();
            }

            // Global click to refocus for external scanners
            $(document).click(function(e) {
                if (!$(e.target).closest('input, button, select, textarea').length) {
                    if ($('#kanbanInput').is(':visible') && !$('#kanbanInput').prop('disabled')) {
                        $('#kanbanInput').focus();
                    } else if ($('#labelInput').is(':visible') && !$('#labelInput').prop('disabled')) {
                        $('#labelInput').focus();
                    } else {
                        $('#tagInput').focus();
                    }
                }
            });

            // Visual feedback for focus
            $('input').on('focus', function() {
                $(this).closest('.custom-cyber-input-group').addClass('selected-glow');
            }).on('blur', function() {
                $(this).closest('.custom-cyber-input-group').removeClass('selected-glow');
            });

            // --- FLOATING BUTTON & SCHEDULE MODAL LOGIC ---
            const scheduleModal = new bootstrap.Modal(document.getElementById('scheduleModal'));
            
            $('#btnShowSchedule').click(function() {
                scheduleModal.show();
                loadScheduleList();
            });

            // Filter Event Listeners
            let filterTimeout;
            $('#filterSchedDate, #filterSchedVin').on('input change', function() {
                clearTimeout(filterTimeout);
                filterTimeout = setTimeout(loadScheduleList, 500);
            });

            function loadScheduleList() {
                const date = $('#filterSchedDate').val();
                const vin = $('#filterSchedVin').val();

                $('#scheduleListBody').html(`
                    <tr>
                        <td colspan="5" class="text-center py-5">
                            <div class="spinner-border text-yellow" role="status"></div>
                            <div class="mt-2">Memuat data...</div>
                        </td>
                    </tr>
                `);

                $.ajax({
                    url: '/PreparationWorkflow/GetSchedulesJson',
                    type: 'GET',
                    data: { filterDate: date, vin: vin },
                    success: function(data) {
                        let html = '';
                        if (data && data.length > 0) {
                            data.forEach(item => {
                                let statusClass = 'bg-secondary';
                                if (item.status === 'Prepared' || item.status === 'Completed') statusClass = 'bg-success';
                                else if (item.status === 'Preparing' || item.status === 'In Progress') statusClass = 'bg-info text-dark';
                                else if (item.status === 'Scheduled' || item.status === 'Waiting') statusClass = 'bg-secondary';

                                // Kanban Fraction Display
                                let actual = item.totalKanbanActual || 0;
                                let target = item.totalKanbanTarget || 0;
                                let progressClass = 'bg-info text-dark';
                                if (actual >= target && target > 0) progressClass = 'bg-success text-white';

                                html += `
                                    <tr>
                                        <td class="p-2 fw-bold text-info">${item.scheduleNumber}</td>
                                        <td class="p-2">
                                            <div class="fw-bold">${item.customerName}</div>
                                            <small class="text-muted"><i class="bi bi-geo-alt"></i> ${item.dock}</small>
                                        </td>
                                        <td class="p-2">
                                            <div class="text-white small" style="white-space: pre-wrap; word-break: break-word; max-width: 250px;">${item.viNs || '-'}</div>
                                        </td>
                                        <td class="p-2 text-center">
                                            <span class="badge ${progressClass} fs-7" style="min-width: 50px;">${actual} / ${target}</span>
                                        </td>
                                        <td class="p-2 text-center">
                                            <span class="badge ${statusClass}" style="font-size: 0.7rem;">${item.status ? item.status.toUpperCase() : '-'}</span>
                                        </td>
                                    </tr>
                                `;
                            });
                        } else {
                            html = '<tr><td colspan="5" class="text-center py-4 text-muted fst-italic">Tidak ada jadwal ditemukan.</td></tr>';
                        }
                        $('#scheduleListBody').html(html);
                    },
                    error: function() {
                        $('#scheduleListBody').html('<tr><td colspan="5" class="text-center py-4 text-danger">Gagal memuat data.</td></tr>');
                    }
                });
            }
            // Initial Fast Mode setup
            if ($('#fastScanMode').is(':checked')) {
                $('#labelInput, #kanbanInput, .btn-scan-trigger').prop('disabled', false);
            }

            $('#fastScanMode').change(function() {
                if ($(this).is(':checked')) {
                    $('#labelInput, #kanbanInput, .btn-scan-trigger').prop('disabled', false);
                } else {
                    // Reset if turned off and inputs are empty
                    if (!$('#tagInput').val()) resetInputs();
                }
            });
        });
    