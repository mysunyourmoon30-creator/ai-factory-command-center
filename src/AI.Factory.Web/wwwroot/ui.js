/*
 * Small UI helpers that need the DOM.
 *
 * Kept separate from theme.js, which has to run synchronously in <head> before first paint;
 * this one only ever runs from a Blazor event handler, so it is deferred with the page.
 */
(function () {
    'use strict';

    /*
     * The master/detail screens (Production Plans, Material Shortage, Procurement) render the
     * detail panel underneath a table that can be a screenful long. Clicking "Detail" on a row
     * near the bottom appeared to do nothing at all, because the panel it opened was below the
     * fold. Only ever called from an event handler, so the circuit is live and there is no
     * prerender to guard against.
     */
    window.aiFactoryScrollIntoView = function (elementId) {
        var element = document.getElementById(elementId);
        if (!element) {
            return;
        }

        var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        element.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' });
    };
})();
