// ==================== SIDEBAR TOGGLE ====================
document.addEventListener('DOMContentLoaded', function () {
    const sidebarToggle = document.getElementById('sidebarToggle');
    const sidebar = document.getElementById('sidebar');
    const mainContent = document.getElementById('mainContent');

    // Check localStorage for sidebar state
    const sidebarCollapsed = localStorage.getItem('sidebarCollapsed') === 'true';
    const sidebarShow = localStorage.getItem('sidebarShow') === 'true';

    if (sidebarCollapsed) {
        sidebar.classList.add('collapsed');
    }
    if (sidebarShow) {
        sidebar.classList.add('show');
    }

    // Toggle sidebar on button click
    if (sidebarToggle) {
        sidebarToggle.addEventListener('click', function () {
            // Desktop toggle
            sidebar.classList.toggle('collapsed');
            const isCollapsed = sidebar.classList.contains('collapsed');
            localStorage.setItem('sidebarCollapsed', isCollapsed);

            // Mobile toggle
            if (window.innerWidth <= 991.98) {
                sidebar.classList.toggle('show');
                const isShow = sidebar.classList.contains('show');
                localStorage.setItem('sidebarShow', isShow);
            }
        });
    }

    // Close sidebar when clicking/tapping outside of it
    const handleOutsideClick = function (event) {
        if (sidebar && sidebarToggle) {
            const isClickInsideSidebar = sidebar.contains(event.target);
            const isClickOnToggle = sidebarToggle.contains(event.target);

            if (!isClickInsideSidebar && !isClickOnToggle) {
                // For Mobile: hide sidebar
                if (window.innerWidth <= 991.98 && sidebar.classList.contains('show')) {
                    sidebar.classList.remove('show');
                    localStorage.setItem('sidebarShow', 'false');
                }
                // For Desktop: collapse sidebar
                else if (window.innerWidth > 991.98 && !sidebar.classList.contains('collapsed')) {
                    sidebar.classList.add('collapsed');
                    localStorage.setItem('sidebarCollapsed', 'true');
                }
            }
        }
    };

    document.addEventListener('click', handleOutsideClick);
    document.addEventListener('touchstart', handleOutsideClick, { passive: true });

    // ==================== CURRENT TIME ====================
    function updateTime() {
        const now = new Date();
        const hours = String(now.getHours()).padStart(2, '0');
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const seconds = String(now.getSeconds()).padStart(2, '0');
        const timeString = `${hours}:${minutes}:${seconds}`;

        const timeElement = document.getElementById('currentTime');
        if (timeElement) {
            timeElement.textContent = timeString;
        }
    }

    // Update time immediately and then every second
    updateTime();
    setInterval(updateTime, 1000);

    // ==================== ACTIVE LINK HIGHLIGHTING ====================
    const currentPath = window.location.pathname;
    const sidebarLinks = document.querySelectorAll('.sidebar-link');

    sidebarLinks.forEach(link => {
        try {
            // Validasi href sebelum membuat URL object
            if (!link.href || link.href === '#' || link.href.trim() === '') {
                return; // Skip jika href tidak valid
            }

            // Coba buat URL object, jika gagal gunakan href langsung
            let linkPath;
            try {
                linkPath = new URL(link.href).pathname;
            } catch (e) {
                // Jika href adalah relative path, gunakan langsung
                linkPath = link.getAttribute('href') || link.href;
                // Hapus query string dan hash jika ada
                linkPath = linkPath.split('?')[0].split('#')[0];
            }

            if (currentPath === linkPath || (linkPath !== '/' && currentPath.startsWith(linkPath))) {
                link.classList.add('active');

                // Keep parent collapse menu open
                const parentCollapse = link.closest('.collapse');
                if (parentCollapse) {
                    parentCollapse.classList.add('show');
                    // Find the trigger button and update aria-expanded
                    const triggerBtn = document.querySelector(`[data-bs-target="#${parentCollapse.id}"]`);
                    if (triggerBtn) {
                        triggerBtn.setAttribute('aria-expanded', 'true');
                        triggerBtn.classList.remove('collapsed');
                    }
                }
            }
        } catch (error) {
            console.warn('Error processing link:', link.href, error);
        }
    });

    // ==================== SMOOTH ANIMATIONS ====================
    // Add entrance animations to cards
    const cards = document.querySelectorAll('.card, .cyber-card');
    const observerOptions = {
        threshold: 0.1,
        rootMargin: '0px 0px -50px 0px'
    };

    const observer = new IntersectionObserver(function (entries) {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.style.opacity = '0';
                entry.target.style.transform = 'translateY(20px)';
                setTimeout(() => {
                    entry.target.style.transition = 'all 0.6s ease';
                    entry.target.style.opacity = '1';
                    entry.target.style.transform = 'translateY(0)';
                }, 100);
                observer.unobserve(entry.target);
            }
        });
    }, observerOptions);

    cards.forEach(card => {
        observer.observe(card);
    });

    // ==================== AUTO-DISMISS ALERTS ====================
    const alerts = document.querySelectorAll('.alert');
    alerts.forEach(alert => {
        setTimeout(() => {
            const bsAlert = new bootstrap.Alert(alert);
            bsAlert.close();
        }, 5000);
    });
});
