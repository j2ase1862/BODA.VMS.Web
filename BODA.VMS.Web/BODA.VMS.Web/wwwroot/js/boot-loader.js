// CSP 'script-src self' 와 호환되도록 외부 파일로 분리 (인라인 스크립트 금지).
// MudLayout / Login 화면이 DOM 에 들어오면 boot loader 오버레이를 페이드 아웃 + 제거.
// fallback: 30 초 후 강제 제거 (WASM 초기화 실패 등 비정상 케이스 대비).
(function () {
    var hide = function () {
        var el = document.getElementById('vms-boot-loader');
        if (!el) return;
        el.style.opacity = '0';
        setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 300);
    };
    var observer = new MutationObserver(function () {
        if (document.querySelector('.mud-layout, .vms-login-bg, .vms-login-card')) {
            hide();
            observer.disconnect();
        }
    });
    observer.observe(document.body, { childList: true, subtree: true });
    setTimeout(function () { hide(); observer.disconnect(); }, 30000);
})();
