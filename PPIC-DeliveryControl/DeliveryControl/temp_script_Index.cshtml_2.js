
    // ============================================
    // INITIALIZATION - HARUS DI AWAL
    // ============================================
    
    // Initialize timeout tracking array - HARUS di awal sebelum fungsi apapun
    if (typeof window.dashboardTimeouts === 'undefined') {
        window.dashboardTimeouts = [];
    }
    
    // Helper untuk track timeout dengan safety check - HARUS didefinisikan di awal
    function safeSetTimeout(callback, delay) {
        // Pastikan array selalu terinisialisasi (double-check untuk safety)
        if (!window.dashboardTimeouts || !Array.isArray(window.dashboardTimeouts)) {
            window.dashboardTimeouts = [];
        }
        
        const timeoutId = setTimeout(() => {
            try {
                callback();
            } catch (error) {
                console.error("Error in safeSetTimeout callback:", error);
            }
            
            // Remove dari array setelah execute (dengan safety check)
            if (window.dashboardTimeouts && Array.isArray(window.dashboardTimeouts)) {
                const index = window.dashboardTimeouts.indexOf(timeoutId);
                if (index > -1) {
                    window.dashboardTimeouts.splice(index, 1);
                }
            }
        }, delay);
        
        // Add ke tracking array (dengan safety check)
        if (window.dashboardTimeouts && Array.isArray(window.dashboardTimeouts)) {
            window.dashboardTimeouts.push(timeoutId);
        }
        
        return timeoutId;
    }
    
    // ============================================
    // SIGNALR REAL-TIME UPDATE CONFIGURATION
    // ============================================
    
    console.log("Initializing SignalR connection...");
    
    // Build SignalR connection
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/deliveryHub")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // Reconnect intervals
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // Connection state management
    let isConnected = false;
    let reconnectAttempts = 0;
    const maxReconnectAttempts = 10;
    let pollingInterval = null;
    let usePollingFallback = false;

    // Toast notification function
    function showToast(message, type = 'info') {
        const toastHtml = `
            <div class="position-fixed top-0 end-0 p-3" style="z-index: 9999;">
                <div class="toast align-items-center text-white bg-${type === 'success' ? 'success' : type === 'danger' ? 'danger' : 'info'} border-0 show" role="alert" aria-live="assertive" aria-atomic="true">
                    <div class="d-flex">
                        <div class="toast-body">
                            <i class="bi bi-${type === 'success' ? 'check-circle-fill' : type === 'danger' ? 'exclamation-triangle-fill' : 'info-circle-fill'}"></i> ${message}
                        </div>
                        <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
                    </div>
                </div>
            </div>
        `;
        $('body').append(toastHtml);
        safeSetTimeout(() => {
            $('.toast').fadeOut(300, function() { $(this).parent().remove(); });
        }, 5000);
    }

    // Update statistics cards dengan error handling dan retry
    let statisticsRetryCount = 0;
    const maxStatisticsRetries = 3;
    
    function updateStatistics() {
        var timestamp = new Date().getTime();
        // Return promise for coordination
        return $.ajax({
            url: '@Url.Action("GetDashboardStatistics", "Home")',
            type: 'GET',
            cache: false,
            timeout: 10000, // 10 second timeout
            data: { _: timestamp },
            success: function(data) {
                console.log("📊 Statistics updated:", data);
                statisticsRetryCount = 0; // Reset retry count on success
                
                // Hitung display text untuk card completed (X / Y)
                var completedDisplay = data.completedCount;
                if (data.todaySchedules && data.todaySchedules > 0) {
                    completedDisplay = data.completedCount + ' / ' + data.todaySchedules;
                }

                // Update card values immediately for speed
                $('#totalTodayStatValue').text(data.todaySchedules);
                $('#completedStatValue').text(completedDisplay);
                $('#delayPickupStatValue').text(data.delayPickupCount);
                $('#delayDockInStatValue').text(data.notArrivedCount);
                $('#delayPrepareStatValue').text(data.delayPrepareCount);
                if ($('#preparedStatValue').length) {
                    $('#preparedStatValue').text(data.preparedCount);
                }
            },
            error: function(err) {
                console.error("Failed to update statistics:", err);
                statisticsRetryCount++;
                
                // Retry dengan exponential backoff
                if (statisticsRetryCount < maxStatisticsRetries) {
                    const retryDelay = Math.min(1000 * Math.pow(2, statisticsRetryCount), 10000);
                    console.log(`🔄 Retrying statistics update in ${retryDelay}ms (attempt ${statisticsRetryCount}/${maxStatisticsRetries})`);
                    safeSetTimeout(updateStatistics, retryDelay);
                } else {
                    console.error("Max retries reached for statistics update");
                    statisticsRetryCount = 0; // Reset untuk next cycle
                }
            }
        });
    }

    // Update table data dengan error handling dan retry
    let tableRetryCount = 0;
    const maxTableRetries = 3;
    
    function updateTableData() {
        var timestamp = new Date().getTime();
        // Return promise for coordination
        return $.ajax({
            url: '@Url.Action("GetScheduleTableData", "Home")',
            type: 'GET',
            cache: false,
            timeout: 15000, // 15 second timeout (lebih lama karena data lebih besar)
            data: { _: timestamp },
            success: function(html) {
                // Update tbody immediately for speed
                $('#table-container tbody').html(html);
                // Re-apply current filter after update
                filterSchedules(currentFilter);
                console.log("Table update complete");
            },
            error: function(err) {
                console.error("Failed to update table:", err);
                console.error("Error details:", err.responseText);
                tableRetryCount++;
                
                // Retry dengan exponential backoff
                if (tableRetryCount < maxTableRetries) {
                    const retryDelay = Math.min(1000 * Math.pow(2, tableRetryCount), 15000);
                    console.log(`🔄 Retrying table update in ${retryDelay}ms (attempt ${tableRetryCount}/${maxTableRetries})`);
                    safeSetTimeout(updateTableData, retryDelay);
                } else {
                    console.error("Max retries reached for table update");
                    tableRetryCount = 0; // Reset untuk next cycle
                }
            }
        });
    }

    // Helper function to update status badge
    function updateStatusBadge(status, text) {
        const badge = $('#signalr-status');
        badge.removeClass('status-connecting status-live status-reconnecting status-disconnected');
        badge.addClass(`status-${status}`);
        badge.find('.status-text').text(text);
    }

    // SignalR event handlers
    connection.on("connected", function(connectionId) {
        console.log("SignalR Connected! Connection ID:", connectionId);
        isConnected = true;
        reconnectAttempts = 0;
        
        // Update connection indicator
        updateStatusBadge('live', 'Live');
    });

    // Listen for Stock updates (often related to preparation)
    connection.on("updateStock", function() {
        console.log("Stock update received! Refreshing dashboard...");
        Promise.all([updateStatistics(), updateTableData()]);
    });

    connection.on("deliveryUpdated", function(data) {
        const timestamp = new Date().toLocaleTimeString();
        console.log(`[${timestamp}] Delivery Update Received:`, data);
        
        // Update connection indicator to show work in progress
        updateStatusBadge('reconnecting', 'Updating...');
        
        // Use camelCase for data properties (SignalR default)
        const action = data.action || data.Action;
        const message = data.message || data.Message;
        
        // Show notification
        const icon = action === 'arrival' ? '🚚' : (action === 'departure' ? '✅' : '🔄');
        showToast(`${icon} ${message}`, 'success');
        
        // Update dashboard data in parallel
        Promise.all([updateStatistics(), updateTableData()]).finally(() => {
            if (isConnected) {
                updateStatusBadge('live', 'Live');
            }
        });
    });

    // Connection lifecycle events
    connection.onreconnecting((error) => {
        console.warn("SignalR reconnecting...", error);
        isConnected = false;
        updateStatusBadge('reconnecting', 'Reconnecting...');
    });

    connection.onreconnected((connectionId) => {
        console.log("SignalR reconnected! Connection ID:", connectionId);
        isConnected = true;
        reconnectAttempts = 0;
        updateStatusBadge('live', 'Live');
        
        // Refresh data after reconnection
        updateStatistics();
        updateTableData();
    });

    connection.onclose((error) => {
        console.error("SignalR connection closed:", error);
        isConnected = false;
        updateStatusBadge('disconnected', 'Disconnected');
        
        // Auto reconnect
        if (reconnectAttempts < maxReconnectAttempts) {
            reconnectAttempts++;
            console.log(`🔄 Attempting to reconnect... (${reconnectAttempts}/${maxReconnectAttempts})`);
            safeSetTimeout(() => startConnection(), 5000);
        } else {
            console.error("❌ Max reconnect attempts reached. Switching to polling fallback.");
            showToast('SignalR tidak tersedia. Menggunakan polling sebagai fallback.', 'warning');
            startPollingFallback();
        }
    });

    // Start connection function
    async function startConnection() {
        try {
            await connection.start();
            console.log("✅ SignalR connection started successfully!");
            isConnected = true;
            reconnectAttempts = 0;
            usePollingFallback = false;
            stopPollingFallback(); // Stop polling if SignalR is working
        } catch (err) {
            console.error("❌ SignalR connection failed:", err);
            isConnected = false;
            
            // After max attempts, switch to polling
            if (reconnectAttempts >= maxReconnectAttempts) {
                console.warn("⚠️ SignalR failed after max attempts. Using polling fallback.");
                startPollingFallback();
            } else {
                reconnectAttempts++;
                safeSetTimeout(() => startConnection(), 5000);
            }
        }
    }

    // Polling fallback function (jika SignalR tidak tersedia)
    function startPollingFallback() {
        if (usePollingFallback) {
            console.log("⚠️ Polling fallback already started, skipping...");
            return; // Already started
        }
        
        // Pastikan SignalR benar-benar stopped sebelum mulai polling
        if (connection && isConnected) {
            try {
                connection.stop().catch(err => console.error("Error stopping SignalR before polling:", err));
                isConnected = false;
            } catch (err) {
                console.error("Error in SignalR stop:", err);
            }
        }
        
        usePollingFallback = true;
        console.log("🔄 Starting polling fallback (every 5 seconds)...");
        updateStatusBadge('reconnecting', 'Polling Mode');
        
        // Clear existing interval if any (double-check)
        if (pollingInterval) {
            clearInterval(pollingInterval);
            pollingInterval = null;
        }
        
        // Immediate update
        updateStatistics();
        updateTableData();
        
        // Then poll every 5 seconds
        pollingInterval = setInterval(function() {
            // Double-check: jika SignalR sudah connect, stop polling
            if (isConnected) {
                console.log("✅ SignalR reconnected, stopping polling fallback");
                stopPollingFallback();
                return;
            }
            
            // Pastikan polling masih aktif
            if (!usePollingFallback) {
                console.log("⚠️ Polling fallback disabled, stopping interval");
                if (pollingInterval) {
                    clearInterval(pollingInterval);
                    pollingInterval = null;
                }
                return;
            }
            
            console.log("🔄 Polling update...");
            updateStatistics();
            updateTableData();
        }, 5000);
    }

    function stopPollingFallback() {
        console.log("🛑 Stopping polling fallback...");
        if (pollingInterval) {
            clearInterval(pollingInterval);
            pollingInterval = null;
        }
        usePollingFallback = false;
        console.log("✅ Polling fallback stopped");
    }

    // Initialize connection on page load
    startConnection();
    
    // Fallback: Jika SignalR tidak connect dalam 10 detik, gunakan polling
    safeSetTimeout(function() {
        if (!isConnected && !usePollingFallback) {
            console.warn("⚠️ SignalR not connected after 10 seconds. Starting polling fallback.");
            startPollingFallback();
        }
    }, 10000);

    // Cleanup on page unload - CRITICAL untuk mencegah memory leak
    function cleanupAllResources() {
        console.log("🧹 Cleaning up all resources...");
        
        // Stop SignalR connection
        if (connection) {
            try {
                if (isConnected) {
                    connection.stop().catch(err => console.error("Error stopping SignalR:", err));
                }
            } catch (err) {
                console.error("Error in SignalR cleanup:", err);
            }
        }
        
        // Clear all intervals
        if (pollingInterval) {
            clearInterval(pollingInterval);
            pollingInterval = null;
        }
        
        if (autoScrollInterval) {
            clearInterval(autoScrollInterval);
            autoScrollInterval = null;
        }
        
        // Clear all pending timeouts (dengan menyimpan reference)
        if (window.dashboardTimeouts && Array.isArray(window.dashboardTimeouts)) {
            window.dashboardTimeouts.forEach(timeout => {
                try {
                    clearTimeout(timeout);
                } catch (err) {
                    console.error("Error clearing timeout:", err);
                }
            });
            window.dashboardTimeouts = [];
        }
        
        console.log("✅ Cleanup complete");
    }
    
    // Cleanup saat page unload
    window.addEventListener('beforeunload', cleanupAllResources);
    
    // Cleanup saat page visibility change (tab hidden/visible)
    document.addEventListener('visibilitychange', function() {
        if (document.hidden) {
            // Tab hidden - bisa pause beberapa operasi untuk hemat resource
            console.log("📱 Tab hidden - pausing non-critical operations");
        } else {
            // Tab visible - resume operations
            console.log("📱 Tab visible - resuming operations");
            // Refresh data saat tab kembali visible
            if (isConnected || usePollingFallback) {
                updateStatistics();
                updateTableData();
            }
        }
    });

    // Manual refresh function
    function manualRefresh() {
        console.log("🔄 Manual refresh triggered");
        updateStatusBadge('reconnecting', 'Refreshing...');
        updateStatistics();
        updateTableData();
        
        // Reset status badge after a moment
        safeSetTimeout(function() {
            if (isConnected) {
                updateStatusBadge('live', 'Live');
            } else if (usePollingFallback) {
                updateStatusBadge('reconnecting', 'Polling Mode');
            } else {
                updateStatusBadge('disconnected', 'Disconnected');
            }
        }, 1000);
    }
    
    // Periodic refresh fallback sudah di-handle oleh polling fallback
    // Tidak perlu interval tambahan karena polling fallback sudah menangani ini

    console.log("SignalR initialization complete!");

    // ============================================
    // AUTO-SCROLL VERTICAL TABLE
    // ============================================
    let autoScrollInterval = null;
    let isScrollingDown = true;
    let isPaused = false;
    let isAutoScrollEnabled = true; // Default enabled
    const scrollSpeed = 1; // pixels per frame
    const scrollDelay = 20; // milliseconds between scrolls
    const STORAGE_KEY = 'dashboard_autoscroll_enabled';

    // Load state from localStorage
    function loadAutoScrollState() {
        const saved = localStorage.getItem(STORAGE_KEY);
        if (saved !== null) {
            isAutoScrollEnabled = saved === 'true';
        }
        updateAutoScrollToggleUI();
    }

    // Update toggle button UI
    function updateAutoScrollToggleUI() {
        const toggleBtn = document.getElementById('autoScrollToggle');
        const toggleIcon = document.getElementById('autoScrollIcon');
        const toggleText = document.getElementById('autoScrollText');
        
        if (toggleBtn && toggleIcon && toggleText) {
            if (isAutoScrollEnabled) {
                toggleBtn.classList.remove('btn-outline-secondary');
                toggleBtn.classList.add('btn-outline-success');
                toggleIcon.classList.remove('bi-pause-circle', 'bi-arrow-down-up');
                toggleIcon.classList.add('bi-arrow-down-up');
                toggleText.textContent = 'Auto Scroll: ON';
            } else {
                toggleBtn.classList.remove('btn-outline-success');
                toggleBtn.classList.add('btn-outline-secondary');
                toggleIcon.classList.remove('bi-arrow-down-up');
                toggleIcon.classList.add('bi-pause-circle');
                toggleText.textContent = 'Auto Scroll: OFF';
            }
        }
    }

    // Toggle auto-scroll on/off
    function toggleAutoScroll() {
        isAutoScrollEnabled = !isAutoScrollEnabled;
        localStorage.setItem(STORAGE_KEY, isAutoScrollEnabled.toString());
        updateAutoScrollToggleUI();
        
        if (isAutoScrollEnabled) {
            startAutoScroll();
        } else {
            stopAutoScroll();
        }
    }

    function startAutoScroll() {
        if (!isAutoScrollEnabled) {
            console.log("⚠️ Auto-scroll disabled, not starting");
            return; // Don't start if disabled
        }
        
        const tableContainer = document.getElementById('table-container');
        if (!tableContainer) {
            console.log("⚠️ Table container not found, cannot start auto-scroll");
            return;
        }

        // Hanya jalankan jika konten lebih tinggi dari container
        if (tableContainer.scrollHeight <= tableContainer.clientHeight) {
            console.log("ℹ️ Content doesn't overflow, auto-scroll not needed");
            return; // Tidak perlu scroll jika konten tidak overflow
        }

        // Hapus interval yang sudah ada jika ada (prevent duplicate)
        if (autoScrollInterval) {
            console.log("⚠️ Auto-scroll interval already exists, clearing first");
            clearInterval(autoScrollInterval);
            autoScrollInterval = null;
        }

        console.log("▶️ Starting auto-scroll");
        autoScrollInterval = setInterval(function() {
            // Double-check enabled state
            if (isPaused || !isAutoScrollEnabled) {
                return;
            }
            
            // Re-check container existence
            const container = document.getElementById('table-container');
            if (!container) {
                console.log("⚠️ Table container removed, stopping auto-scroll");
                stopAutoScroll();
                return;
            }

            const maxScroll = container.scrollHeight - container.clientHeight;
            const currentScroll = container.scrollTop;

            if (isScrollingDown) {
                // Scroll ke bawah
                if (currentScroll < maxScroll) {
                    container.scrollTop += scrollSpeed;
                } else {
                    // Sudah sampai bawah, mulai scroll ke atas
                    isScrollingDown = false;
                }
            } else {
                // Scroll ke atas
                if (currentScroll > 0) {
                    container.scrollTop -= scrollSpeed;
                } else {
                    // Sudah sampai atas, mulai scroll ke bawah
                    isScrollingDown = true;
                }
            }
        }, scrollDelay);
    }

    function stopAutoScroll() {
        if (autoScrollInterval) {
            console.log("🛑 Stopping auto-scroll");
            clearInterval(autoScrollInterval);
            autoScrollInterval = null;
        }
    }

    // Initialize auto-scroll setelah DOM ready
    document.addEventListener('DOMContentLoaded', function() {
        // Load saved state
        loadAutoScrollState();
        
        // Setup toggle button
        const toggleBtn = document.getElementById('autoScrollToggle');
        if (toggleBtn) {
            toggleBtn.addEventListener('click', toggleAutoScroll);
        }
        
        // Tunggu sebentar untuk memastikan tabel sudah ter-render
        safeSetTimeout(function() {
            if (isAutoScrollEnabled) {
                startAutoScroll();
            }
        }, 1000);

        // Pause saat hover, resume saat tidak hover
        const tableContainer = document.getElementById('table-container');
        if (tableContainer) {
            tableContainer.addEventListener('mouseenter', function() {
                isPaused = true;
            });

            tableContainer.addEventListener('mouseleave', function() {
                isPaused = false;
            });
        }
    });

    // Restart auto-scroll setelah update tabel (untuk SignalR update)
    const originalUpdateTableData = updateTableData;
    updateTableData = function() {
        const promise = originalUpdateTableData();
        
        // Restart auto-scroll setelah tabel di-update (dengan delay kecil agar DOM render selesai)
        if (promise && typeof promise.then === 'function') {
            promise.then(() => {
                safeSetTimeout(function() {
                    stopAutoScroll();
                    if (isAutoScrollEnabled) {
                        startAutoScroll();
                    }
                }, 500);
            });
        } else {
            safeSetTimeout(function() {
                stopAutoScroll();
                if (isAutoScrollEnabled) {
                    startAutoScroll();
                }
            }, 500);
        }
        
        return promise;
    };
