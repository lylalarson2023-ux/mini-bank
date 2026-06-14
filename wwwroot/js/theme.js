window.adnTheme = {
    get: function () {
        return localStorage.getItem('adn_theme') || 'dark';
    },
    set: function (theme) {
        localStorage.setItem('adn_theme', theme);
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
    var theme = window.adnTheme.get();
    document.documentElement.setAttribute('data-theme', theme);
})();


