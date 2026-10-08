// Sistem Packing — Global JS Utilities

// AJAX CSRF setup
$.ajaxSetup({
    beforeSend: function (xhr) {
        const token = $('input[name="__RequestVerificationToken"]').val()
            || $('meta[name="csrf-token"]').attr('content');
        if (token) xhr.setRequestHeader('RequestVerificationToken', token);
    }
});

// Toast notification helper
window.showToast = function (type, message, duration = 3000) {
    Swal.fire({
        icon: type,
        title: message,
        toast: true,
        position: 'top-end',
        showConfirmButton: false,
        timer: duration,
        timerProgressBar: true
    });
};

// Format number with thousand separator
window.formatNumber = function (num) {
    return new Intl.NumberFormat('id-ID').format(num);
};

// Session timeout warning (5 minutes before timeout)
let sessionTimer = null;
function resetSessionTimer() {
    clearTimeout(sessionTimer);
    sessionTimer = setTimeout(function () {
        Swal.fire({
            icon: 'warning',
            title: 'Sesi Hampir Habis',
            text: 'Sesi Anda akan berakhir dalam 5 menit. Klik OK untuk melanjutkan.',
            confirmButtonText: 'Lanjutkan',
            confirmButtonColor: '#4f6ef7'
        }).then(function (result) {
            if (result.isConfirmed) {
                fetch('/Dashboard/GetKpi', { credentials: 'same-origin' }).catch(() => {});
                resetSessionTimer();
            }
        });
    }, 25 * 60 * 1000); // 25 minutes
}

// Reset timer on user activity
['click', 'keypress', 'scroll', 'mousemove'].forEach(ev => {
    document.addEventListener(ev, resetSessionTimer, { passive: true });
});
resetSessionTimer();
