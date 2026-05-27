window.mbankTheme = {
    get: function () {
        return localStorage.getItem('mbank_theme') || 'dark';
    },
    set: function (theme) {
        localStorage.setItem('mbank_theme', theme);
        document.documentElement.setAttribute('data-theme', theme);
    },
    toggle: function () {
        var current = this.get();
        var next = current === 'dark' ? 'light' : 'dark';
        this.set(next);
        return next;
    }
};

(function () {
    var theme = window.mbankTheme.get();
    document.documentElement.setAttribute('data-theme', theme);
})();
