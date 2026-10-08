
    let currentFilter = 'all';

    function filterSchedules(filter) {
        currentFilter = filter;
        
        // Update active card
        document.querySelectorAll('.filter-card').forEach(card => {
            card.classList.remove('active');
        });
        const activeCard = document.querySelector(`[data-filter="${filter}"]`);
        if (activeCard) {
            activeCard.classList.add('active');
        }

        // Show/hide reset button
        const resetBtn = document.getElementById('resetFilterBtn');
        if (resetBtn) {
            resetBtn.style.display = filter === 'all' ? 'none' : 'inline-block';
        }

        // Filter table rows
        const rows = document.querySelectorAll('.schedule-row');
        let visibleCount = 0;

        rows.forEach(row => {
            const status = row.getAttribute('data-status');
            const hasArrived = row.getAttribute('data-arrived') === 'true';
            const isDelayPickup = row.getAttribute('data-delaypickup') === 'true';
            const isDelayPrepare = row.getAttribute('data-delayprepare') === 'true';
            let shouldShow = false;

            if (filter === 'all') {
                shouldShow = true;
            } else if (filter === 'completed') {
                shouldShow = status === 'completed';
            } else if (filter === 'inprogress') {
                shouldShow = status === 'inprogress';
            } else if (filter === 'delaypickup') {
                shouldShow = isDelayPickup;
            } else if (filter === 'delayprepare') {
                shouldShow = isDelayPrepare === true; // Filter untuk Delay Prepare (Slow Work)
            } else if (filter === 'notarrived') {
                shouldShow = row.getAttribute('data-notarrived') === 'true'; // Filter untuk Delay Dock In
            } else if (filter === 'prepared') {
                shouldShow = row.getAttribute('data-prepared') === 'true';
            }

            if (shouldShow) {
                row.classList.remove('hidden');
                visibleCount++;
            } else {
                row.classList.add('hidden');
            }
        });
        
        // Debug log untuk Delay Dock In filter
        if (filter === 'delayprepare' || filter === 'notarrived') {
            console.log(`🔍 Filter Delay Dock In: ${visibleCount} schedule(s) ditemukan`);
        }

        // Update row numbers without overriding customer info
        let rowNumber = 1;
        rows.forEach(row => {
            const badge = row.querySelector('.row-number-text');
            if (!badge)
                return;

            if (!row.classList.contains('hidden')) {
                badge.textContent = rowNumber++;
            }
            else {
                badge.textContent = '';
            }
        });

        // Show/hide empty state
        const tableWrapper = document.getElementById('table-wrapper');
        const cardBody = document.getElementById('main-card-body');
        
        // Hapus semua empty state filter yang mungkin ada (cleanup dulu)
        const existingEmptyStates = document.querySelectorAll('.empty-state-filter-wrapper');
        existingEmptyStates.forEach(el => el.remove());
        
        if (visibleCount === 0) {
            // Sembunyikan wrapper tabel (termasuk table dan button)
            if (tableWrapper) {
                tableWrapper.style.display = 'none';
            }
            
            // Tampilkan empty state baru
            if (cardBody) {
                const newEmptyState = document.createElement('div');
                newEmptyState.className = 'text-center py-5 empty-state-filter-wrapper';
                newEmptyState.innerHTML = `
                    <div class="empty-state-filter">
                        <i class="bi bi-filter-circle" style="font-size: 4rem; color: var(--primary-blue); opacity: 0.5;"></i>
                        <h4 class="mt-3 mb-2">Tidak Ada Data untuk Filter Ini</h4>
                        <p class="text-muted mb-4">Klik card lain untuk melihat data yang berbeda.</p>
                    </div>
                `;
                cardBody.appendChild(newEmptyState);
            }
        } else {
            // Tampilkan wrapper tabel (termasuk table dan button)
            if (tableWrapper) {
                tableWrapper.style.display = 'block';
            }
        }
    }

    // Initialize on page load
    document.addEventListener('DOMContentLoaded', function() {
        filterSchedules('all');
    });
