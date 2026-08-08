/*
 * Colour theme switching.
 *
 * Loaded synchronously from <head> so the stored theme is applied to <html> before first
 * paint - deferring it produces a visible flash of the light theme on every navigation.
 *
 * Deliberately plain JS with no Blazor interop: the toggle then works during prerender and
 * before the Interactive Server circuit connects, and it survives a circuit drop. The button
 * calls window.aiFactoryToggleTheme directly.
 */
(function () {
    'use strict';

    var STORAGE_KEY = 'ai-factory-theme';

    function storedTheme() {
        try {
            var saved = window.localStorage.getItem(STORAGE_KEY);
            return saved === 'light' || saved === 'dark' ? saved : null;
        } catch (e) {
            // Private browsing / storage disabled - fall back to the OS preference.
            return null;
        }
    }

    function preferredTheme() {
        return storedTheme()
            || (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    }

    function apply(theme) {
        var root = document.documentElement;
        root.setAttribute('data-bs-theme', theme);
        // Tells the browser to render native controls and scrollbars to match.
        root.style.colorScheme = theme;
    }

    window.aiFactoryToggleTheme = function () {
        var next = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
        try {
            window.localStorage.setItem(STORAGE_KEY, next);
        } catch (e) {
            // Not persisting is acceptable; the toggle still works for this page.
        }
        apply(next);
    };

    apply(preferredTheme());

    // Follow the OS only while the user has not made an explicit choice.
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (event) {
        if (!storedTheme()) {
            apply(event.matches ? 'dark' : 'light');
        }
    });
})();
