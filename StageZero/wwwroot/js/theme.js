// Persists the StageZero light/dark choice per browser. Read after first
// render by AppVM; absent or unreadable storage falls back to dark.
window.stageZeroTheme = {
    get: function () {
        try { return localStorage.getItem("stagezero.theme"); } catch { return null; }
    },
    set: function (value) {
        try { localStorage.setItem("stagezero.theme", value); } catch { }
    }
};
