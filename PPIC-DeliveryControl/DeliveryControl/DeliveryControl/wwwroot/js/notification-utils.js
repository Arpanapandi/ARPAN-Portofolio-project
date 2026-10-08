/**
 * notification-utils.js
 * Global Notification Utilities:
 *  1. showUpdateNotification() — slide kanan ke kiri (bukan kedip)
 *  2. showScanToast()          — toast scan preparation yang terbaca jelas
 */

(function () {
    'use strict';

    // ══════════════════════════════════════════════════════
    // 1. NOTIFIKASI SLIDE KANAN KE KIRI
    // ══════════════════════════════════════════════════════

    function ensureNotifContainer() {
        var c = document.getElementById('update-notif-container');
        if (!c) {
            c = document.createElement('div');
            c.id = 'update-notif-container';
            c.style.cssText = [
                'position:fixed',
                'top:75px',
                'right:0',
                'z-index:99999',
                'display:flex',
                'flex-direction:column',
                'gap:8px',
                'pointer-events:none',
                'max-width:400px',
                'padding-right:0'
            ].join(';');
            document.body.appendChild(c);
        }
        return c;
    }

    var notifIconMap = {
        pulling:  '📦',
        prep:     '🔄',
        delivery: '🚚',
        warning:  '⚠️',
        success:  '✅',
        danger:   '❌',
        info:     'ℹ️'
    };

    var notifTitleMap = {
        pulling:  'Stock Masuk',
        prep:     'Preparation',
        delivery: 'Delivery Update',
        warning:  'Peringatan',
        success:  'Berhasil',
        danger:   'Error',
        info:     'Informasi'
    };

    var notifColorMap = {
        pulling:  { border: '#00e676', title: '#00e676' },
        prep:     { border: '#ffd600', title: '#ffd600' },
        delivery: { border: '#00c6ff', title: '#00c6ff' },
        warning:  { border: '#ff9800', title: '#ff9800' },
        success:  { border: '#00e676', title: '#00e676' },
        danger:   { border: '#f44336', title: '#f44336' },
        info:     { border: '#40c4ff', title: '#40c4ff' }
    };

    /**
     * Tampilkan notifikasi update — slide dari kanan ke kiri
     * @param {string} message  - Pesan yang ditampilkan
     * @param {string} type     - 'pulling' | 'prep' | 'delivery' | 'warning' | 'success' | 'danger' | 'info'
     * @param {number} duration - Durasi tampil dalam ms (default 5000)
     */
    window.showUpdateNotification = function (message, type, duration) {
        type = type || 'info';
        duration = duration || 5000;
        // Guard: jangan tampilkan jika message kosong/undefined
        if (!message || message === 'undefined') return;

        var container = ensureNotifContainer();
        var color = notifColorMap[type] || notifColorMap.info;
        var icon = notifIconMap[type] || 'ℹ️';
        var title = notifTitleMap[type] || 'Notifikasi';
        var now = new Date().toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

        var el = document.createElement('div');
        el.style.cssText = [
            'background:linear-gradient(135deg,#1a2a4a 0%,#0d1b35 100%)',
            'border-left:4px solid ' + color.border,
            'border-radius:10px 0 0 10px',
            'padding:12px 14px',
            'color:#fff',
            'font-size:13px',
            'display:flex',
            'align-items:flex-start',
            'gap:10px',
            'pointer-events:all',
            'box-shadow:-4px 4px 24px rgba(0,0,0,0.45)',
            'transform:translateX(110%)',
            'opacity:0',
            'transition:transform 0.4s cubic-bezier(0.25,0.46,0.45,0.94),opacity 0.4s ease',
            'min-width:280px',
            'max-width:400px',
            'width:100%'
        ].join(';');

        el.innerHTML =
            '<span style="font-size:18px;flex-shrink:0;margin-top:1px;">' + icon + '</span>' +
            '<div style="flex:1;min-width:0;">' +
                '<div style="font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.5px;color:' + color.title + ';margin-bottom:3px;">' + title + '</div>' +
                '<div style="font-size:13px;color:#e0e8f0;line-height:1.4;word-wrap:break-word;">' + message + '</div>' +
                '<div style="font-size:10px;color:#7a9bbf;margin-top:4px;">' + now + '</div>' +
            '</div>' +
            '<button onclick="this.parentElement.remove()" style="background:none;border:none;color:#7a9bbf;cursor:pointer;font-size:16px;padding:0;flex-shrink:0;line-height:1;" title="Tutup">✕</button>';

        container.appendChild(el);

        // Animasi masuk: kanan → kiri
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                el.style.transform = 'translateX(0)';
                el.style.opacity = '1';
            });
        });

        // Auto hide
        setTimeout(function () {
            el.style.transform = 'translateX(110%)';
            el.style.opacity = '0';
            setTimeout(function () {
                if (el.parentElement) el.remove();
            }, 450);
        }, duration);
    };

    // ══════════════════════════════════════════════════════
    // 2. SCAN TOAST — Preparation (Kontras Tinggi, Terbaca)
    // ══════════════════════════════════════════════════════

    var _scanToastTimer = null;
    var _scanToastEl = null;

    function ensureScanToast() {
        if (!_scanToastEl || !document.body.contains(_scanToastEl)) {
            _scanToastEl = document.createElement('div');
            _scanToastEl.id = 'globalScanToast';
            _scanToastEl.style.cssText = [
                'position:fixed',
                'bottom:32px',
                'left:50%',
                'transform:translateX(-50%) translateY(20px)',
                'z-index:999999',
                'min-width:300px',
                'max-width:520px',
                'border-radius:12px',
                'padding:14px 22px',
                'font-size:15px',
                'font-weight:700',
                'text-align:center',
                'box-shadow:0 8px 40px rgba(0,0,0,0.6)',
                'display:none',
                'pointer-events:none',
                'letter-spacing:0.3px',
                'transition:opacity 0.25s ease,transform 0.25s ease',
                'opacity:0'
            ].join(';');
            document.body.appendChild(_scanToastEl);
        }
        return _scanToastEl;
    }

    var scanToastStyles = {
        success: { bg: '#00c853', border: '#69f0ae', color: '#ffffff' },
        danger:  { bg: '#b71c1c', border: '#f44336', color: '#ffffff' },
        warning: { bg: '#e65100', border: '#ffab40', color: '#ffffff' },
        info:    { bg: '#01579b', border: '#40c4ff', color: '#ffffff' },
        primary: { bg: '#1565c0', border: '#42a5f5', color: '#ffffff' }
    };

    var scanToastIcons = {
        success: '✅',
        danger:  '❌',
        warning: '⚠️',
        info:    'ℹ️',
        primary: '📋'
    };

    /**
     * Tampilkan toast notifikasi scan preparation
     * @param {string} message  - Pesan
     * @param {string} type     - 'success' | 'danger' | 'warning' | 'info' | 'primary'
     * @param {number} duration - ms (default 3500)
     */
    window.showScanToast = function (message, type, duration) {
        type = type || 'info';
        duration = duration || 3500;

        var toast = ensureScanToast();
        var style = scanToastStyles[type] || scanToastStyles.info;
        var icon = scanToastIcons[type] || 'ℹ️';

        toast.style.background = style.bg;
        toast.style.border = '2px solid ' + style.border;
        toast.style.color = style.color;
        toast.style.textShadow = '0 1px 4px rgba(0,0,0,0.4)';
        toast.innerHTML = '<span style="font-size:19px;margin-right:10px;vertical-align:middle;">' + icon + '</span>' + message;

        toast.style.display = 'block';
        toast.style.opacity = '0';
        toast.style.transform = 'translateX(-50%) translateY(20px)';

        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                toast.style.opacity = '1';
                toast.style.transform = 'translateX(-50%) translateY(0)';
            });
        });

        if (_scanToastTimer) clearTimeout(_scanToastTimer);
        _scanToastTimer = setTimeout(function () {
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(-50%) translateY(20px)';
            setTimeout(function () { toast.style.display = 'none'; }, 260);
        }, duration);
    };

})();
