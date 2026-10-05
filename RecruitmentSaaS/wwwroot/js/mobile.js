/* Phone layer for every page that uses _Layout (paired with mobile.css).
   - Tables become cards: each cell gets data-label from its column header.
   - Anything still wider than the screen (fixed widths, rows that don't wrap) is tamed.
   Nothing here changes desktop: the table classes only take effect inside the CSS media query,
   and the overflow fixes only run on narrow screens. Opt out with class "no-stack" / "m-keep". */
(function () {
    'use strict';

    var PHONE = window.matchMedia('(max-width: 767.98px)');

    function isActionCell(td) {
        // Only buttons/links/forms, no real text of its own
        if (!td.querySelector('button, .btn, form, a')) return false;
        var clone = td.cloneNode(true);
        clone.querySelectorAll('button, .btn, form, a, select, input, i, .dropdown-menu').forEach(function (n) { n.remove(); });
        return clone.textContent.trim().length === 0;
    }

    function labelRow(row, labels) {
        var cells = row.cells;
        if (cells.length === 1 && cells[0].colSpan > 1) {          // "nothing here" row
            row.classList.add('m-empty');
            cells[0].setAttribute('data-label', '');
            return;
        }
        var col = 0;
        for (var i = 0; i < cells.length; i++) {
            var td = cells[i];
            if (!td.hasAttribute('data-label')) td.setAttribute('data-label', labels[col] || '');
            if (isActionCell(td)) td.classList.add('m-actions');
            col += td.colSpan || 1;
        }
    }

    function wrapForScroll(table) {
        var p = table.parentElement;
        if (!p || p.classList.contains('m-scroll')) return;
        var ov = getComputedStyle(p).overflowX;
        if (p.classList.contains('table-responsive') || ov === 'auto' || ov === 'scroll') return;
        var wrap = document.createElement('div');
        wrap.className = 'm-scroll';
        p.insertBefore(wrap, table);
        wrap.appendChild(table);
    }

    function stackTable(table) {
        if (table.dataset.mDone) return;
        table.dataset.mDone = '1';
        if (table.classList.contains('no-stack') || table.closest('.no-stack') || table.classList.contains('users-table')) return;

        var head = table.tHead;
        var complex = !head || head.rows.length !== 1
            || table.querySelector('thead th[colspan], tbody td[rowspan], tbody th[rowspan]')
            || table.querySelector('tbody table');                     // nested tables
        if (complex) { wrapForScroll(table); return; }

        var labels = [];
        Array.prototype.forEach.call(head.rows[0].cells, function (th) {
            var text = th.textContent.replace(/\s+/g, ' ').trim();
            for (var k = 0; k < (th.colSpan || 1); k++) labels.push(text);
        });

        Array.prototype.forEach.call(table.tBodies, function (tb) {
            Array.prototype.forEach.call(tb.rows, function (r) { labelRow(r, labels); });
            // Rows added later (AJAX, filters) get labels too
            new MutationObserver(function (muts) {
                muts.forEach(function (m) {
                    m.addedNodes.forEach(function (n) { if (n.nodeName === 'TR') labelRow(n, labels); });
                });
            }).observe(tb, { childList: true });
        });
        table.classList.add('m-stack');
    }

    function px(v) { var n = parseFloat(v); return isNaN(n) ? 0 : n; }

    // Fixed widths / grids / rows that are wider than the phone screen
    function tameOverflow(root) {
        if (!PHONE.matches) return;
        var limit = root.clientWidth || window.innerWidth;

        root.querySelectorAll('[style*="width"]').forEach(function (el) {
            if (el.closest('.m-keep, .m-scroll, .m-stack')) return;
            var s = el.style;
            if (px(s.minWidth) > limit - 8) s.minWidth = '0';
            if (px(s.width) > limit - 8 && !/%/.test(s.width)) { s.width = '100%'; }
            if (px(s.maxWidth) > limit) s.maxWidth = '100%';
        });

        root.querySelectorAll('*').forEach(function (el) {
            if (el.closest('.m-keep, .m-scroll, .m-stack')) return;
            if (el.scrollWidth <= el.clientWidth + 2) return;
            var cs = getComputedStyle(el);
            if (cs.overflowX === 'auto' || cs.overflowX === 'scroll' || cs.overflowX === 'hidden') return;
            if (cs.display === 'grid' && cs.gridTemplateColumns.split(' ').length > 1) {
                el.style.gridTemplateColumns = 'repeat(auto-fit, minmax(140px, 1fr))';
            } else if ((cs.display === 'flex' || cs.display === 'inline-flex') && cs.flexWrap === 'nowrap' && cs.flexDirection.indexOf('row') === 0) {
                el.style.flexWrap = 'wrap';
            }
        });
    }

    function run() {
        var root = document.querySelector('.page-body');
        if (!root || document.getElementById('inbox-app')) return;   // the WhatsApp inbox has its own phone UI
        root.querySelectorAll('table').forEach(stackTable);
        tameOverflow(root);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', run);
    else run();
    window.addEventListener('load', function () { var r = document.querySelector('.page-body'); if (r) tameOverflow(r); });
    PHONE.addEventListener && PHONE.addEventListener('change', function () { var r = document.querySelector('.page-body'); if (r) tameOverflow(r); });
})();
