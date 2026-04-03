// Blazor circuit: UserSpecifiedDisplay updates #components-seconds-to-next-attempt / #components-reconnect-current-attempt
// and toggles classes on #components-reconnect-modal (see aspnetcore UserSpecifiedDisplay.ts).
(function () {
    function syncReconnectMessages() {
        var secEl = document.getElementById('components-seconds-to-next-attempt');
        var attEl = document.getElementById('components-reconnect-current-attempt');
        var lineRejoin = document.getElementById('mqtt-reconnect-msg-rejoin');
        var lineRetry = document.getElementById('mqtt-reconnect-msg-retry');
        var unitEl = document.getElementById('mqtt-reconnect-sec-unit');
        if (!secEl || !attEl || !lineRejoin || !lineRetry) {
            return;
        }
        var s = parseInt(secEl.textContent.trim(), 10);
        var a = parseInt(attEl.textContent.trim(), 10);
        if (Number.isNaN(s)) {
            s = 0;
        }
        if (Number.isNaN(a)) {
            a = 1;
        }
        var showRejoin = a === 1 || s === 0;
        lineRejoin.hidden = !showRejoin;
        lineRetry.hidden = showRejoin;
        if (unitEl) {
            unitEl.textContent = s === 1 ? 'second' : 'seconds';
        }
    }

    function wire() {
        var modal = document.getElementById('components-reconnect-modal');
        var secEl = document.getElementById('components-seconds-to-next-attempt');
        var attEl = document.getElementById('components-reconnect-current-attempt');
        var retryBtn = document.getElementById('mqtt-reconnect-retry-btn');

        if (secEl) {
            new MutationObserver(syncReconnectMessages).observe(secEl, { characterData: true, childList: true, subtree: true });
        }
        if (attEl) {
            new MutationObserver(syncReconnectMessages).observe(attEl, { characterData: true, childList: true, subtree: true });
        }

        if (modal) {
            new MutationObserver(function () {
                if (modal.classList.contains('components-reconnect-rejected')) {
                    location.reload();
                    return;
                }
                var visible = modal.classList.contains('components-reconnect-show') ||
                    modal.classList.contains('components-reconnect-failed');
                modal.setAttribute('aria-hidden', visible ? 'false' : 'true');
            }).observe(modal, { attributes: true, attributeFilter: ['class'] });
        }

        if (retryBtn) {
            retryBtn.addEventListener('click', function () {
                if (window.Blazor && typeof window.Blazor.reconnect === 'function') {
                    window.Blazor.reconnect();
                }
            });
        }

        syncReconnectMessages();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', wire);
    } else {
        wire();
    }
})();
