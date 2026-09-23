// WhatsApp Shared Inbox — vanilla JS (no frontend framework exists in this project, so we
// stay consistent with the rest of the app: fetch() + server-rendered shell + SignalR).
(function () {
    'use strict';

    var USER = window.__INBOX_USER__ || {};
    var IS_MOBILE = window.matchMedia('(max-width: 991px)').matches;

    var STATUS_LABEL = { 1: 'جديد', 2: 'مفتوح', 3: 'بانتظار العميل', 4: 'متابعة', 5: 'مغلق' };
    var LEAD_STAGE_LABEL = { 1: 'جديد', 2: 'تم التواصل', 3: 'مهتم', 4: 'متابعة', 5: 'مؤهل', 6: 'تم الفوز', 7: 'خسارة' };
    var LEAD_STAGE_OPTIONS = [1, 2, 3, 4, 5, 6, 7];
    var MSG_STATUS_ICON = { 2: 'bi-clock', 3: 'bi-check', 4: 'bi-check-all', 5: 'bi-check-all', 6: 'bi-exclamation-circle-fill' };

    var state = {
        accounts: [],
        currentAccountId: null,
        currentFilter: 'all',
        searchTerm: '',
        searchDebounce: null,
        conversations: [],
        selectedConversationId: null,
        selectedConversation: null,
        oldestLoadedAt: null,
        hasMoreMessages: true,
        loadingMessages: false,
        sending: false,
        hub: null
    };

    // ── Helpers ──────────────────────────────────────────────────────────────
    function esc(s) {
        if (s === null || s === undefined) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function initials(name) {
        if (!name) return '؟';
        return name.trim().charAt(0).toUpperCase();
    }

    function timeAgo(iso) {
        if (!iso) return '';
        var d = new Date(iso);
        var diffMs = Date.now() - d.getTime();
        var mins = Math.floor(diffMs / 60000);
        if (mins < 1) return 'الآن';
        if (mins < 60) return mins + 'د';
        var hrs = Math.floor(mins / 60);
        if (hrs < 24) return hrs + 'س';
        var days = Math.floor(hrs / 24);
        if (days < 7) return days + 'ي';
        return d.toLocaleDateString('ar-EG', { day: 'numeric', month: 'short' });
    }

    function formatClock(iso) {
        return new Date(iso).toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' });
    }

    function formatDateSeparator(iso) {
        var d = new Date(iso);
        var today = new Date();
        var yest = new Date(); yest.setDate(today.getDate() - 1);
        if (d.toDateString() === today.toDateString()) return 'اليوم';
        if (d.toDateString() === yest.toDateString()) return 'أمس';
        return d.toLocaleDateString('ar-EG', { day: 'numeric', month: 'long', year: 'numeric' });
    }

    async function api(url, options) {
        options = options || {};
        options.headers = Object.assign({ 'Content-Type': 'application/json' }, options.headers || {});
        var res = await fetch(url, options);
        if (!res.ok) {
            var body = null;
            try { body = await res.json(); } catch (e) { /* ignore */ }
            throw { status: res.status, body: body };
        }
        if (res.status === 204) return null;
        return res.json();
    }

    // ── Account tabs ─────────────────────────────────────────────────────────
    async function loadAccounts() {
        try {
            state.accounts = await api('/api/whatsapp/accounts');
        } catch (e) {
            state.accounts = [];
        }
        renderAccountTabs();
    }

    function renderAccountTabs() {
        var wrap = document.getElementById('account-tabs');
        var totalUnread = state.accounts.reduce(function (sum, a) { return sum + a.unreadCount; }, 0);

        var html = '<button class="account-tab' + (state.currentAccountId === null ? ' active' : '') + '" data-account="">' +
            'الكل' + (totalUnread > 0 ? '<span class="account-tab-badge">' + totalUnread + '</span>' : '') + '</button>';

        state.accounts.forEach(function (a) {
            html += '<button class="account-tab' + (state.currentAccountId === a.id ? ' active' : '') + '" data-account="' + a.id + '">' +
                esc(a.name) + (a.unreadCount > 0 ? '<span class="account-tab-badge">' + a.unreadCount + '</span>' : '') + '</button>';
        });

        wrap.innerHTML = html;
        wrap.querySelectorAll('.account-tab').forEach(function (btn) {
            btn.addEventListener('click', function () {
                state.currentAccountId = btn.dataset.account || null;
                renderAccountTabs();
                loadConversations();
            });
        });
    }

    // ── Conversation list ────────────────────────────────────────────────────
    function buildListQuery() {
        var params = new URLSearchParams();
        if (state.currentAccountId) params.set('accountId', state.currentAccountId);
        if (state.searchTerm) params.set('search', state.searchTerm);

        switch (state.currentFilter) {
            case 'unread': params.set('unreadOnly', 'true'); break;
            case 'me': params.set('assigned', 'me'); break;
            case 'unassigned': params.set('assigned', 'unassigned'); break;
            case 'open': params.set('status', '2'); break;
            case 'followup': params.set('status', '4'); break;
            case 'closed': params.set('status', '5'); break;
        }
        return params.toString();
    }

    async function loadConversations() {
        var listEl = document.getElementById('conversation-list');
        try {
            var data = await api('/api/conversations?' + buildListQuery());
            state.conversations = data.items;
            renderConversationList();
        } catch (e) {
            listEl.innerHTML = '<div class="inbox-empty-state"><i class="bi bi-exclamation-triangle"></i><div>تعذر تحميل المحادثات</div></div>';
        }
    }

    function renderConversationList() {
        var listEl = document.getElementById('conversation-list');

        if (!state.conversations.length) {
            listEl.innerHTML = '<div class="inbox-empty-state"><i class="bi bi-inbox"></i><div>لا توجد محادثات</div></div>';
            return;
        }

        listEl.innerHTML = state.conversations.map(function (c) {
            var preview = c.lastMessagePreview ? esc(c.lastMessagePreview) : (c.lastMessageDirection === 2 ? 'أنت: —' : '—');
            var agent = c.assignedSalesAgentName ? esc(c.assignedSalesAgentName) : 'غير مخصص';
            return '' +
                '<div class="conversation-row' + (c.unreadCount > 0 ? ' unread' : '') + (c.id === state.selectedConversationId ? ' selected' : '') + '" data-id="' + c.id + '">' +
                '  <div class="conv-avatar">' + initials(c.contactName) + '</div>' +
                '  <div class="conv-body">' +
                '    <div class="conv-top-row"><span class="conv-name">' + esc(c.contactName) + '</span><span class="conv-time">' + timeAgo(c.lastMessageAt) + '</span></div>' +
                '    <div class="conv-preview">' + (c.lastMessageDirection === 2 ? 'أنت: ' : '') + preview + '</div>' +
                '    <div class="conv-meta-row">' +
                '      <span class="conv-account-pill">' + esc(c.whatsAppAccountName) + '</span>' +
                '      <span class="conv-agent-pill"><i class="bi bi-person"></i> ' + agent + '</span>' +
                (c.leadCode ? '<span class="conv-agent-pill">' + esc(c.leadCode) + '</span>' : '') +
                '    </div>' +
                '  </div>' +
                (c.unreadCount > 0 ? '<span class="conv-unread-badge">' + c.unreadCount + '</span>' : '') +
                '</div>';
        }).join('');

        listEl.querySelectorAll('.conversation-row').forEach(function (row) {
            row.addEventListener('click', function () { selectConversation(row.dataset.id); });
        });
    }

    function patchListRow(conversationId, patch) {
        var conv = state.conversations.find(function (c) { return c.id === conversationId; });
        if (!conv) { loadConversations(); return; }
        Object.assign(conv, patch);
        renderConversationList();
    }

    // ── Conversation selection / chat ───────────────────────────────────────
    async function selectConversation(id) {
        state.selectedConversationId = id;
        state.oldestLoadedAt = null;
        state.hasMoreMessages = true;

        document.getElementById('chat-empty-state').style.display = 'none';
        document.getElementById('chat-active').style.display = 'flex';
        document.getElementById('chat-messages').innerHTML = '<div class="inbox-empty-state"><i class="bi bi-hourglass-split"></i></div>';

        renderConversationList();
        showScreen('chat');

        try {
            var detail = await api('/api/conversations/' + id);
            state.selectedConversation = detail;
            renderChatHeader(detail);
            renderCustomerPanel(detail);
            await loadMessages(true);
            markRead(id);
            watchConversation(id);
        } catch (e) {
            document.getElementById('chat-messages').innerHTML = '<div class="inbox-empty-state"><i class="bi bi-exclamation-triangle"></i><div>تعذر تحميل المحادثة</div></div>';
        }
    }

    function renderChatHeader(c) {
        document.getElementById('chat-avatar').textContent = initials(c.contactName);
        document.getElementById('chat-header-name').textContent = c.contactName;
        document.getElementById('chat-header-sub').textContent = c.contactPhone + (c.assignedSalesAgentName ? ' · ' + c.assignedSalesAgentName : ' · غير مخصص');
        document.getElementById('chat-header-account').textContent = c.whatsAppAccountName;
    }

    async function loadMessages(reset) {
        if (state.loadingMessages || (!reset && !state.hasMoreMessages)) return;
        state.loadingMessages = true;

        var url = '/api/conversations/' + state.selectedConversationId + '/messages';
        if (!reset && state.oldestLoadedAt) url += '?before=' + encodeURIComponent(state.oldestLoadedAt);

        try {
            var data = await api(url);
            state.hasMoreMessages = data.hasMore;
            if (data.messages.length) state.oldestLoadedAt = data.messages[0].whatsAppTimestamp;

            var container = document.getElementById('chat-messages');
            if (reset) container.innerHTML = '';

            var html = buildMessagesHtml(data.messages);
            if (reset) {
                container.innerHTML = html || '<div class="inbox-empty-state"><i class="bi bi-chat-dots"></i><div>لا توجد رسائل بعد</div></div>';
                container.scrollTop = container.scrollHeight;
            } else {
                var prevHeight = container.scrollHeight;
                container.insertAdjacentHTML('afterbegin', html);
                container.scrollTop = container.scrollHeight - prevHeight;
            }
        } catch (e) { /* leave what's already rendered */ }

        state.loadingMessages = false;
    }

    function buildMessagesHtml(messages) {
        var html = '';
        var lastDate = null;
        messages.forEach(function (m) {
            var dateKey = new Date(m.whatsAppTimestamp).toDateString();
            if (dateKey !== lastDate) {
                html += '<div class="chat-date-separator">' + formatDateSeparator(m.whatsAppTimestamp) + '</div>';
                lastDate = dateKey;
            }
            html += renderBubble(m);
        });
        return html;
    }

    function renderBubble(m) {
        var isOut = m.direction === 2;
        var statusIcon = isOut && MSG_STATUS_ICON[m.status]
            ? '<i class="bi ' + MSG_STATUS_ICON[m.status] + (m.status === 6 ? ' bubble-status-failed' : '') + ' bubble-status-icon"></i>' : '';
        var body = m.textBody ? esc(m.textBody) : mediaPlaceholder(m);
        return '' +
            '<div class="chat-bubble-row ' + (isOut ? 'outgoing' : 'incoming') + '" data-message-id="' + m.id + '">' +
            '  <div class="chat-bubble">' + body +
            '    <div class="bubble-meta"><span>' + formatClock(m.whatsAppTimestamp) + '</span>' + statusIcon + '</div>' +
            '  </div>' +
            '</div>';
    }

    function mediaPlaceholder(m) {
        var icons = { 2: 'bi-image', 3: 'bi-camera-video', 4: 'bi-mic', 5: 'bi-file-earmark', 6: 'bi-emoji-smile', 7: 'bi-geo-alt', 8: 'bi-person-vcard', 9: 'bi-ui-checks' };
        var icon = icons[m.messageType] || 'bi-question-circle';
        return '<i class="bi ' + icon + '"></i> <em>مرفق غير نصي</em>';
    }

    function updateBubbleStatus(payload) {
        var row = document.querySelector('.chat-bubble-row[data-message-id="' + payload.messageId + '"] .bubble-meta');
        if (!row) return;
        var icon = MSG_STATUS_ICON[payload.status];
        if (!icon) return;
        var iconEl = row.querySelector('.bubble-status-icon');
        if (!iconEl) { iconEl = document.createElement('i'); iconEl.className = 'bubble-status-icon'; row.appendChild(iconEl); }
        iconEl.className = 'bi ' + icon + (payload.status === 6 ? ' bubble-status-failed' : '') + ' bubble-status-icon';
    }

    async function markRead(id) {
        try { await api('/api/conversations/' + id + '/read', { method: 'POST' }); } catch (e) { /* non-fatal */ }
        patchListRow(id, { unreadCount: 0 });
        loadAccounts();
    }

    // ── Composer ─────────────────────────────────────────────────────────────
    async function sendMessage() {
        var input = document.getElementById('composer-input');
        var text = input.value.trim();
        if (!text || state.sending || !state.selectedConversationId) return;

        state.sending = true;
        var btn = document.getElementById('composer-send-btn');
        btn.disabled = true;

        try {
            var message = await api('/api/conversations/' + state.selectedConversationId + '/messages', {
                method: 'POST',
                body: JSON.stringify({ text: text })
            });
            input.value = '';
            autoGrow(input);
            var container = document.getElementById('chat-messages');
            var lastSeparator = container.querySelector('.chat-date-separator:last-of-type');
            var today = formatDateSeparator(new Date().toISOString());
            if (!lastSeparator || lastSeparator.textContent !== today) {
                container.insertAdjacentHTML('beforeend', '<div class="chat-date-separator">' + today + '</div>');
            }
            container.insertAdjacentHTML('beforeend', renderBubble(message));
            container.scrollTop = container.scrollHeight;
            patchListRow(state.selectedConversationId, { lastMessagePreview: text, lastMessageDirection: 2, lastMessageAt: message.whatsAppTimestamp });
        } catch (e) {
            alert('تعذر إرسال الرسالة' + (e.body && e.body.error ? ': ' + e.body.error : ''));
        } finally {
            state.sending = false;
            btn.disabled = false;
        }
    }

    function autoGrow(el) {
        el.style.height = 'auto';
        el.style.height = Math.min(el.scrollHeight, 120) + 'px';
    }

    // ── Customer / Lead panel ────────────────────────────────────────────────
    function renderCustomerPanel(c) {
        var body = document.getElementById('customer-panel-body');

        var agentField = USER.canAssign
            ? '<select id="cp-agent-select" class="form-select form-select-sm mt-1"><option value="">— غير مخصص —</option></select>'
            : '<div class="customer-field-value">' + (c.assignedSalesAgentName ? esc(c.assignedSalesAgentName) : 'غير مخصص') + '</div>';

        var stageOptions = LEAD_STAGE_OPTIONS.map(function (v) {
            return '<option value="' + v + '"' + (v === c.leadStage ? ' selected' : '') + '>' + LEAD_STAGE_LABEL[v] + '</option>';
        }).join('');

        body.innerHTML = '' +
            '<div class="customer-avatar-lg">' + initials(c.contactName) + '</div>' +
            '<div style="text-align:center;font-weight:700;font-size:15px">' + esc(c.contactName) + '</div>' +
            '<div style="text-align:center;color:var(--text-muted);font-size:12px;margin-bottom:6px">' + esc(c.contactPhone) + '</div>' +

            '<div class="customer-field"><span class="customer-field-label">رقم الواتساب المستقبِل</span><span class="customer-field-value">' + esc(c.whatsAppAccountName) + '</span></div>' +
            '<div class="customer-field"><span class="customer-field-label">المندوب المسؤول</span>' + agentField + '</div>' +
            '<div class="customer-field"><span class="customer-field-label">حالة المحادثة</span>' +
            '  <select id="cp-status-select" class="form-select form-select-sm mt-1">' +
            Object.keys(STATUS_LABEL).map(function (k) { return '<option value="' + k + '"' + (Number(k) === c.status ? ' selected' : '') + '>' + STATUS_LABEL[k] + '</option>'; }).join('') +
            '  </select></div>' +
            '<div class="customer-field"><span class="customer-field-label">مرحلة الصفقة</span>' +
            '  <select id="cp-stage-select" class="form-select form-select-sm mt-1">' + stageOptions + '</select>' +
            '  <input type="text" id="cp-lost-reason" class="form-control form-control-sm mt-1" placeholder="سبب الخسارة" style="display:' + (c.leadStage === 7 ? 'block' : 'none') + '" value="' + esc(c.lostReason || '') + '" />' +
            '</div>' +
            (c.leadCode ? '<div class="customer-field"><span class="customer-field-label">كود العميل المحتمل</span><span class="customer-field-value">' + esc(c.leadCode) + (c.leadFullName ? ' — ' + esc(c.leadFullName) : '') + '</span></div>' : '') +

            '<div class="panel-section-title">ملاحظات داخلية</div>' +
            '<div id="cp-notes-list"><div class="skeleton-line" style="width:80%"></div></div>' +
            '<textarea id="cp-note-input" class="form-control form-control-sm mt-2" rows="2" placeholder="أضف ملاحظة داخلية (لن تُرسل للعميل)"></textarea>' +
            '<button id="cp-note-add-btn" class="btn btn-sm btn-primary mt-2 w-100">إضافة ملاحظة</button>' +

            '<div class="panel-section-title">المتابعات</div>' +
            '<div class="d-flex gap-1 flex-wrap mb-2">' +
            '  <button class="btn btn-sm btn-ghost" data-quick-followup="today">اليوم لاحقاً</button>' +
            '  <button class="btn btn-sm btn-ghost" data-quick-followup="tomorrow">غداً</button>' +
            '  <button class="btn btn-sm btn-ghost" data-quick-followup="3days">خلال 3 أيام</button>' +
            '  <button class="btn btn-sm btn-ghost" data-quick-followup="week">الأسبوع القادم</button>' +
            '</div>' +
            '<div id="cp-followups-list"></div>';

        if (USER.canAssign) populateAgentSelect(c.assignedSalesAgentId);

        document.getElementById('cp-status-select').addEventListener('change', function (e) { updateStatus(c.id, Number(e.target.value)); });
        document.getElementById('cp-stage-select').addEventListener('change', function (e) {
            var stage = Number(e.target.value);
            document.getElementById('cp-lost-reason').style.display = stage === 7 ? 'block' : 'none';
            if (stage !== 7) updateLeadStage(c.id, stage, null);
        });
        document.getElementById('cp-lost-reason').addEventListener('blur', function (e) {
            if (Number(document.getElementById('cp-stage-select').value) === 7) updateLeadStage(c.id, 7, e.target.value.trim());
        });
        document.getElementById('cp-note-add-btn').addEventListener('click', function () { addNote(c.id); });
        body.querySelectorAll('[data-quick-followup]').forEach(function (btn) {
            btn.addEventListener('click', function () { createFollowUp(c.id, btn.dataset.quickFollowup); });
        });

        loadNotes(c.id);
    }

    var agentsCache = null;
    async function populateAgentSelect(currentAgentId) {
        var select = document.getElementById('cp-agent-select');
        if (!select) return;
        select.addEventListener('change', function () {
            assignConversation(state.selectedConversationId, select.value || null);
        });

        if (!agentsCache) {
            try { agentsCache = await api('/api/whatsapp/accounts/agents'); }
            catch (e) { agentsCache = []; }
        }

        agentsCache.forEach(function (agent) {
            var opt = document.createElement('option');
            opt.value = agent.id;
            opt.textContent = agent.fullName;
            if (agent.id === currentAgentId) opt.selected = true;
            select.appendChild(opt);
        });
    }

    async function updateStatus(id, status) {
        try { await api('/api/conversations/' + id + '/status', { method: 'PATCH', body: JSON.stringify({ status: status }) }); }
        catch (e) { alert('تعذر تحديث الحالة'); }
    }

    async function updateLeadStage(id, stage, lostReason) {
        try {
            await api('/api/conversations/' + id + '/leadstage', { method: 'PATCH', body: JSON.stringify({ leadStage: stage, lostReason: lostReason }) });
        } catch (e) { alert('تعذر تحديث المرحلة' + (e.body && e.body.error ? ': ' + e.body.error : '')); }
    }

    async function assignConversation(id, agentId) {
        try {
            await api('/api/conversations/' + id + '/assign', { method: 'POST', body: JSON.stringify({ agentId: agentId }) });
            loadConversations();
        } catch (e) { alert('تعذر تعيين المحادثة'); }
    }

    async function loadNotes(id) {
        var list = document.getElementById('cp-notes-list');
        try {
            var notes = await api('/api/conversations/' + id + '/notes');
            list.innerHTML = notes.length
                ? notes.map(function (n) {
                    return '<div class="note-item">' + esc(n.body) + '<div class="note-meta">' + esc(n.authorName) + ' · ' + timeAgo(n.createdAt) + '</div></div>';
                }).join('')
                : '<div style="font-size:11.5px;color:var(--text-muted)">لا توجد ملاحظات</div>';
        } catch (e) { list.innerHTML = ''; }
    }

    async function addNote(id) {
        var input = document.getElementById('cp-note-input');
        var body = input.value.trim();
        if (!body) return;
        try {
            await api('/api/conversations/' + id + '/notes', { method: 'POST', body: JSON.stringify({ body: body }) });
            input.value = '';
            loadNotes(id);
        } catch (e) { alert('تعذر إضافة الملاحظة'); }
    }

    function quickFollowUpDate(kind) {
        var d = new Date();
        switch (kind) {
            case 'today': d.setHours(d.getHours() + 3); break;
            case 'tomorrow': d.setDate(d.getDate() + 1); d.setHours(10, 0, 0, 0); break;
            case '3days': d.setDate(d.getDate() + 3); d.setHours(10, 0, 0, 0); break;
            case 'week': d.setDate(d.getDate() + 7); d.setHours(10, 0, 0, 0); break;
        }
        return d.toISOString();
    }

    async function createFollowUp(id, kind) {
        try {
            await api('/api/conversations/' + id + '/followups', { method: 'POST', body: JSON.stringify({ dueAt: quickFollowUpDate(kind) }) });
            alert('تم إنشاء متابعة');
        } catch (e) { alert('تعذر إنشاء المتابعة'); }
    }

    // ── Mobile screen navigation ─────────────────────────────────────────────
    function showScreen(name) {
        var columns = document.getElementById('inbox-columns');
        columns.classList.remove('screen-chat', 'screen-customer');
        if (name === 'chat') columns.classList.add('screen-chat');
        if (name === 'customer') columns.classList.add('screen-customer');
    }

    // ── SignalR realtime ─────────────────────────────────────────────────────
    function connectSignalR() {
        if (typeof signalR === 'undefined') return;

        state.hub = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/inbox')
            .withAutomaticReconnect()
            .build();

        state.hub.on('NewMessage', function (payload) {
            loadAccounts();
            if (payload.conversationId === state.selectedConversationId) {
                var container = document.getElementById('chat-messages');
                if (container) {
                    container.insertAdjacentHTML('beforeend', renderBubble(payload));
                    container.scrollTop = container.scrollHeight;
                }
                if (payload.direction === 1) markRead(payload.conversationId);
            }
            loadConversations();
        });

        state.hub.on('MessageStatusUpdated', function (payload) { updateBubbleStatus(payload); });

        state.hub.on('UnreadCountUpdated', function (payload) {
            loadAccounts();
            patchListRow(payload.conversationId, { unreadCount: payload.unreadCount });
        });

        state.hub.on('ConversationAssigned', function () { loadConversations(); });
        state.hub.on('ConversationUpdated', function (payload) {
            loadConversations();
            if (payload.conversationId === state.selectedConversationId) selectConversation(payload.conversationId);
        });

        state.hub.start().catch(function () { /* SignalR is a progressive enhancement — polling fallback below still runs */ });
    }

    function watchConversation(id) {
        if (state.hub && state.hub.state === 'Connected') {
            state.hub.invoke('WatchConversation', id).catch(function () { /* ignore */ });
        }
    }

    // ── Wiring ───────────────────────────────────────────────────────────────
    function bindEvents() {
        document.getElementById('filter-row').addEventListener('click', function (e) {
            var chip = e.target.closest('.inbox-filter-chip');
            if (!chip) return;
            document.querySelectorAll('.inbox-filter-chip').forEach(function (c) { c.classList.remove('active'); });
            chip.classList.add('active');
            state.currentFilter = chip.dataset.filter;
            loadConversations();
        });

        document.getElementById('conv-search').addEventListener('input', function (e) {
            clearTimeout(state.searchDebounce);
            var value = e.target.value;
            state.searchDebounce = setTimeout(function () { state.searchTerm = value; loadConversations(); }, 300);
        });

        var input = document.getElementById('composer-input');
        input.addEventListener('input', function () { autoGrow(input); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });
        document.getElementById('composer-send-btn').addEventListener('click', sendMessage);

        document.getElementById('chat-messages').addEventListener('scroll', function (e) {
            if (e.target.scrollTop < 60) loadMessages(false);
        });

        // Lightweight fallback so the list/badges stay fresh even if SignalR can't connect
        // (e.g. behind a proxy that blocks WebSockets) — matches the existing 30s notif poll.
        setInterval(function () {
            loadAccounts();
            if (!state.selectedConversationId) loadConversations();
        }, 30000);

        window.addEventListener('resize', function () { IS_MOBILE = window.matchMedia('(max-width: 991px)').matches; });
    }

    function init() {
        loadAccounts();
        loadConversations();
        bindEvents();
        connectSignalR();
    }

    document.addEventListener('DOMContentLoaded', init);

    window.Inbox = { showScreen: showScreen };
})();
