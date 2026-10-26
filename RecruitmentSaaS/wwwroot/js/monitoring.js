(function () {
    'use strict';
    var form = document.getElementById('monitor-filters');
    var from = document.getElementById('monitor-from');
    var to = document.getElementById('monitor-to');
    var status = document.getElementById('monitor-status');
    var teams = document.getElementById('monitor-teams');
    var totals = document.getElementById('monitor-totals');
    var fields = [['activeChats', 'محادثات نشطة'], ['bookings', 'حجوزات'], ['visits', 'حضور'], ['lost', 'خسارة'], ['unreadNow', 'غير مقروء']];
    function element(tag, text, cls) {
        var el = document.createElement(tag);
        if (text != null) el.textContent = text;
        if (cls) el.className = cls;
        return el;
    }
    function chatsLink(label, key, id) {
        var link = element('a', label, 'btn btn-sm btn-outline-primary');
        link.href = '/Inbox/Index' + (id ? '?' + new URLSearchParams([[key, id]]).toString() : '');
        return link;
    }
    function render(data) {
        totals.replaceChildren();
        fields.concat([['waitingNow', 'في انتظار التعيين']]).forEach(function (f) {
            var col = element('div', null, 'col-6 col-lg-2');
            var card = element('div', null, 'card card-body h-100');
            card.append(element('span', f[1], 'text-muted small'), element('strong', data.totals[f[0]], 'fs-4'));
            col.append(card); totals.append(col);
        });
        teams.replaceChildren();
        data.teams.forEach(function (team) {
            var card = element('section', null, 'card mb-4');
            var header = element('div', null, 'card-header d-flex flex-wrap justify-content-between gap-2');
            var title = element('div');
            title.append(element('h2', team.name, 'h5 mb-1'), element('span', team.numbers.join(' · ') || 'بدون رقم خاص', 'text-muted small'));
            header.append(title, chatsLink('محادثات الفريق', 'team', team.id));
            var body = element('div', null, 'card-body');
            body.append(element('p', fields.map(function (f) { return f[1] + ': ' + team[f[0]]; }).concat(['في انتظار التعيين: ' + team.waitingNow]).join(' · '), 'text-muted'));
            var wrap = element('div', null, 'table-responsive');
            var table = element('table', null, 'table table-hover align-middle mb-0');
            var head = element('tr');
            ['المندوب'].concat(fields.map(function (f) { return f[1]; }), ['متوسط أول رد', 'المحادثات']).forEach(function (label) {
                var th = element('th', label); th.scope = 'col'; head.append(th);
            });
            var thead = element('thead'); thead.append(head); table.append(thead);
            var tbody = element('tbody');
            team.members.forEach(function (member) {
                var row = element('tr');
                row.append(element('td', member.name + (member.isActive ? '' : ' (معطّل)')));
                fields.forEach(function (f) { row.append(element('td', member[f[0]])); });
                row.append(element('td', member.avgFirstReplyMinutes == null ? '—' : member.avgFirstReplyMinutes + ' دقيقة'));
                var linkCell = element('td'); linkCell.append(chatsLink('عرض المحادثات', 'agent', member.id)); row.append(linkCell); tbody.append(row);
            });
            if (!team.members.length) {
                var empty = element('tr'); var cell = element('td', 'لا يوجد مندوبون في هذا الفريق', 'text-muted');
                cell.colSpan = fields.length + 3; empty.append(cell); tbody.append(empty);
            }
            table.append(tbody); wrap.append(table); body.append(wrap); card.append(header, body); teams.append(card);
        });
        if (!data.teams.length) teams.append(element('p', 'لا توجد فرق لعرضها.', 'text-muted'));
    }
    var requestNumber = 0;
    async function load() {
        var number = ++requestNumber;
        status.textContent = 'جارٍ تحميل النتائج...';
        try {
            var query = new URLSearchParams();
            if (from.value) query.set('from', from.value);
            if (to.value) query.set('to', to.value);
            var response = await fetch('/api/whatsapp/dashboard/teams?' + query, { credentials: 'same-origin' });
            if (!response.ok || response.redirected) throw new Error('load');
            var data = await response.json();
            if (number !== requestNumber) return;
            from.value = data.from; to.value = data.to;
            render(data);
            status.textContent = 'النتائج من ' + data.from + ' إلى ' + data.to + ' · يتم التحديث كل دقيقة';
        } catch (e) {
            if (number === requestNumber) status.textContent = 'تعذر تحديث النتائج. حاول مرة أخرى.';
        }
    }
    form.addEventListener('submit', function (event) { event.preventDefault(); load(); });
    setInterval(function () { if (!document.hidden) load(); }, 60000);
    load();
})();
