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
        agentFilter: '',          // '' | 'agent:<id>' | 'team:<managerId>'  (admins/managers only)
        searchTerm: '',
        searchDebounce: null,
        conversations: [],
        selectedConversationId: null,
        selectedConversation: null,
        oldestLoadedAt: null,
        hasMoreMessages: true,
        loadingMessages: false,
        hub: null,
        panelDirty: false,        // customer panel has unsaved agent/status/stage changes
        panelSavedAt: 0
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

    // Chat list time, like WhatsApp: clock time today, "أمس", weekday this week, else the date
    function listTime(iso) {
        if (!iso) return '';
        var d = new Date(iso), now = new Date();
        var startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
        var dayDiff = Math.floor((startOfToday - new Date(d.getFullYear(), d.getMonth(), d.getDate())) / 86400000);
        if (dayDiff <= 0) return d.toLocaleTimeString('ar-EG', { hour: 'numeric', minute: '2-digit' });
        if (dayDiff === 1) return 'أمس';
        if (dayDiff < 7) return d.toLocaleDateString('ar-EG', { weekday: 'long' });
        return d.toLocaleDateString('ar-EG', { day: 'numeric', month: 'numeric', year: '2-digit' });
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
            case 'open': params.set('status', '2'); break;
            case 'followup': params.set('status', '4'); break;
            case 'closed': params.set('status', '5'); break;
        }

        if (state.agentFilter === 'unassigned') params.set('assigned', 'unassigned');
        else if (state.agentFilter.indexOf('agent:') === 0) params.set('assigned', state.agentFilter.slice(6));
        else if (state.agentFilter.indexOf('team:') === 0) params.set('team', state.agentFilter.slice(5));
        return params.toString();
    }

    // ── Salesperson / team filter (Admin + TeleSales Manager) ────────────────
    async function getAgents() {
        if (!agentsCache) {
            try { agentsCache = await api('/api/whatsapp/accounts/agents'); }
            catch (e) { agentsCache = []; }
        }
        return agentsCache;
    }

    async function initAgentFilter() {
        var select = document.getElementById('agent-filter');
        if (!select || !USER.canAssign) return;

        var agents = await getAgents();

        // Group agents by team (their manager); agents without a manager go last
        var teams = {}, order = [], noTeam = [];
        agents.forEach(function (a) {
            if (!a.managerId || !a.managerName) { noTeam.push(a); return; }
            if (!teams[a.managerId]) { teams[a.managerId] = { name: a.managerName, agents: [] }; order.push(a.managerId); }
            teams[a.managerId].agents.push(a);
        });
        order.sort(function (x, y) { return teams[x].name.localeCompare(teams[y].name, 'ar'); });

        var html = '<option value="">كل المندوبين</option>' +
            '<option value="unassigned" id="pending-option">⏳ في انتظار التعيين</option>';
        if (USER.role === '7' && teams[USER.id]) html += '<option value="team:' + esc(USER.id) + '">👥 فريقي</option>';
        if (USER.role === '8') html += '<option value="agent:' + esc(USER.id) + '">💬 محادثاتي أنا</option>'; // the head's own chats
        order.forEach(function (mgrId) {
            var t = teams[mgrId];
            html += '<optgroup label="فريق ' + esc(t.name) + '">' +
                '<option value="team:' + esc(mgrId) + '">👥 كل فريق ' + esc(t.name) + '</option>';
            t.agents.forEach(function (a) { html += '<option value="agent:' + esc(a.id) + '">' + esc(a.fullName) + '</option>'; });
            html += '</optgroup>';
        });
        if (noTeam.length) {
            html += '<optgroup label="بدون فريق">';
            noTeam.forEach(function (a) { html += '<option value="agent:' + esc(a.id) + '">' + esc(a.fullName) + '</option>'; });
            html += '</optgroup>';
        }
        select.innerHTML = html;
        select.value = state.agentFilter || '';
        refreshPendingCount();

        select.addEventListener('change', function () {
            state.agentFilter = select.value;
            loadConversations();
        });
    }

    function setFilterChip(filter) {
        state.currentFilter = filter;
        document.querySelectorAll('.inbox-filter-chip').forEach(function (c) {
            c.classList.toggle('active', c.dataset.filter === filter);
        });
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
                '    <div class="conv-top-row"><span class="conv-name">' + esc(c.contactName) + '</span><span class="conv-time">' + listTime(c.lastMessageAt) + '</span></div>' +
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
        var isNewSelection = id !== state.selectedConversationId;
        if (isNewSelection) state.panelDirty = false; // unsaved edits belonged to the previous chat
        state.selectedConversationId = id;
        state.oldestLoadedAt = null;
        state.hasMoreMessages = true;

        document.getElementById('chat-empty-state').style.display = 'none';
        document.getElementById('chat-active').style.display = 'flex';
        document.getElementById('chat-messages').innerHTML = '<div class="inbox-empty-state"><i class="bi bi-hourglass-split"></i></div>';

        renderConversationList();
        if (isNewSelection || currentScreen() === 'list') showScreen('chat');

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
                afterRender(container);
                container.scrollTop = container.scrollHeight;
            } else {
                var prevHeight = container.scrollHeight;
                container.insertAdjacentHTML('afterbegin', html);
                afterRender(container);
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

    function mediaUrl(m) {
        if (m.localUrl) return m.localUrl; // optimistic bubble: the file is still in the browser
        return '/api/conversations/' + (m.conversationId || state.selectedConversationId) + '/messages/' + m.id + '/media';
    }

    function renderMedia(m) {
        var hasFile = m.localUrl || m.hasMedia;
        if (!hasFile) return '';
        var url = esc(mediaUrl(m));
        switch (m.messageType) {
            case 2: // image
                return '<a class="bubble-image-link" href="' + url + '" target="_blank" rel="noopener"><img class="bubble-image" src="' + url + '" loading="lazy" alt="صورة" /></a>';
            case 6: // sticker
                return '<img class="bubble-sticker" src="' + url + '" loading="lazy" alt="ملصق" />';
            case 3: // video
                return '<video class="bubble-video" src="' + url + '" controls preload="metadata" playsinline></video>';
            case 4: // voice note / audio
                return '<div class="voice-note" data-src="' + url + '">' +
                    '<button type="button" class="vn-play" aria-label="تشغيل"><i class="bi bi-play-fill"></i></button>' +
                    '<div class="vn-track"><div class="vn-progress"></div><div class="vn-knob"></div></div>' +
                    '<span class="vn-time">0:00</span>' +
                    '</div>';
            case 5: // document
                return '<a class="bubble-doc" href="' + url + '" target="_blank" rel="noopener"><i class="bi bi-file-earmark-text-fill"></i><span>' + esc(m.textBody || 'مستند') + '</span><i class="bi bi-download"></i></a>';
        }
        return '';
    }

    // Like WhatsApp: a message's direction follows its first letter (English chats read left-to-right)
    function textDir(text) {
        var m = /[A-Za-z\u00C0-\u024F]|[\u0590-\u08FF\uFB1D-\uFEFC]/.exec(text || '');
        return m && /[A-Za-z\u00C0-\u024F]/.test(m[0]) ? 'ltr' : 'rtl';
    }

    function renderBubble(m) {
        var isOut = m.direction === 2;
        var type = m.messageType || 1;
        var statusIcon = isOut && MSG_STATUS_ICON[m.status]
            ? '<i class="bi ' + MSG_STATUS_ICON[m.status] + (m.status === 6 ? ' bubble-status-failed' : '') + (m.status === 5 ? ' bubble-status-read' : '') + ' bubble-status-icon"></i>' : '';

        var media = renderMedia(m);
        var hasMediaShown = media !== '';
        // For media the text is a caption; documents already show their name inside the file chip
        var text = (type === 1 || type === 7 || type === 8 || type === 9 || !hasMediaShown)
            ? (m.textBody || (hasMediaShown ? '' : mediaPlaceholderText(m)))
            : (type === 5 ? '' : (m.textBody || ''));

        var failure = isOut && m.status === 6
            ? '<div class="bubble-error"><i class="bi bi-exclamation-triangle-fill"></i> لم تُرسل' + (m.errorMessage ? ': ' + esc(m.errorMessage) : '') + '</div>'
            : '';
        // Who on our side sent it — several agents/managers can write in the same chat
        var sender = isOut && m.senderUserName
            ? '<div class="bubble-sender"><i class="bi bi-person-fill"></i> ' + esc(m.senderUserName) + '</div>'
            : '';
        var meta = '<span class="bubble-meta"><span>' + formatClock(m.whatsAppTimestamp) + '</span>' + statusIcon + '</span>';

        var classes = 'chat-bubble' + (hasMediaShown ? ' has-media media-' + type : '') + (text ? ' has-text' : '');
        // Built without whitespace between tags: the bubble keeps whitespace (for line breaks in messages)
        return '<div class="chat-bubble-row ' + (isOut ? 'outgoing' : 'incoming') + '" data-message-id="' + m.id + '"' +
            (isOut && m.senderUserName ? ' data-sender="' + esc(m.senderUserName) + '"' : '') + '>' +
            '<div class="' + classes + '" dir="' + (text && !hasMediaShown ? textDir(text) : 'rtl') + '">' + sender + media +
            (text ? '<span class="bubble-text">' + esc(text) + '</span>' : '') +
            meta + failure +
            '</div>' +
            (m.id && m.status !== 6 && m.status !== 2 && (text || hasMediaShown)
                ? '<button type="button" class="bubble-forward-btn" data-forward="' + m.id + '" title="تحويل الرسالة لمحادثة تانية"><i class="bi bi-forward-fill"></i></button>'
                : '') +
            '</div>';
    }

    // The same message can arrive several times: the send response plus one realtime event per
    // SignalR group this user is in (org-wide, the open conversation, the assigned agent).
    function afterRender(container) {
        var prev = null;
        container.querySelectorAll('.chat-bubble-row, .chat-date-separator').forEach(function (el) {
            if (el.classList.contains('chat-bubble-row')) {
                var same = prev && prev.classList.contains('chat-bubble-row') &&
                    prev.classList.contains('outgoing') && el.classList.contains('outgoing') &&
                    prev.dataset.sender && prev.dataset.sender === el.dataset.sender;
                el.classList.toggle('same-sender', !!same);
            }
            prev = el;
        });
    }

    function lastDateSeparator(container) {
        var all = container.querySelectorAll('.chat-date-separator');
        return all.length ? all[all.length - 1] : null;
    }

    function ensureTodaySeparator(container) {
        var empty = container.querySelector('.inbox-empty-state');
        if (empty) empty.remove();
        var today = formatDateSeparator(new Date().toISOString());
        var last = lastDateSeparator(container);
        if (!last || last.textContent !== today)
            container.insertAdjacentHTML('beforeend', '<div class="chat-date-separator">' + today + '</div>');
    }

    function appendBubble(container, m) {
        if (m.id && container.querySelector('.chat-bubble-row[data-message-id="' + m.id + '"]')) return;
        // Our own just-sent message: replace its pending placeholder instead of adding a second bubble
        if (m.direction === 2) {
            var key = (m.messageType || 1) + '|' + (m.textBody || '');
            var pending = Array.prototype.find.call(container.querySelectorAll('.chat-bubble-row[data-pending-key]'),
                function (row) { return row.dataset.pendingKey === key; });
            if (pending) { replacePending(pending, m); afterRender(container); return; }
        }
        container.insertAdjacentHTML('beforeend', renderBubble(m));
        afterRender(container);
        container.scrollTop = container.scrollHeight;
    }

    // Swap an optimistic bubble for the saved message; keep showing the local file meanwhile
    function replacePending(row, m) {
        var localUrl = row.dataset.localUrl;
        row.outerHTML = renderBubble(localUrl && !m.hasMedia ? Object.assign({}, m, { localUrl: localUrl }) : m);
    }

    function mediaPlaceholderText(m) {
        var labels = { 2: '📷 صورة', 3: '🎥 فيديو', 4: '🎤 رسالة صوتية', 5: '📄 مستند', 6: 'ملصق', 7: '📍 موقع', 8: '👤 جهة اتصال', 9: 'رد تفاعلي' };
        return labels[m.messageType] || 'رسالة غير مدعومة';
    }

    function updateBubbleStatus(payload) {
        var row = document.querySelector('.chat-bubble-row[data-message-id="' + payload.messageId + '"] .bubble-meta');
        if (!row) return;
        var icon = MSG_STATUS_ICON[payload.status];
        if (!icon) return;
        var iconEl = row.querySelector('.bubble-status-icon');
        if (!iconEl) { iconEl = document.createElement('i'); iconEl.className = 'bubble-status-icon'; row.appendChild(iconEl); }
        iconEl.className = 'bi ' + icon + (payload.status === 6 ? ' bubble-status-failed' : '') + (payload.status === 5 ? ' bubble-status-read' : '') + ' bubble-status-icon';
    }

    async function markRead(id) {
        try { await api('/api/conversations/' + id + '/read', { method: 'POST' }); } catch (e) { /* non-fatal */ }
        patchListRow(id, { unreadCount: 0 });
        loadAccounts();
    }

    // ── Composer ─────────────────────────────────────────────────────────────
    // Optimistic send: the bubble shows immediately with a clock icon and the box clears; it is
    // swapped for the saved message when the server (or its realtime event) confirms it.
    var pendingSeq = 0;
    async function sendMessage() {
        var input = document.getElementById('composer-input');
        var text = input.value.trim();
        var conversationId = state.selectedConversationId;
        if (!text || !conversationId) return;

        input.value = '';
        autoGrow(input);
        input.focus();

        var container = document.getElementById('chat-messages');
        ensureTodaySeparator(container);

        var tempId = 'pending-' + (++pendingSeq);
        container.insertAdjacentHTML('beforeend', renderBubble({
            id: tempId, direction: 2, status: 2, textBody: text,
            senderUserName: USER.name, whatsAppTimestamp: new Date().toISOString()
        }));
        var tempRow = container.querySelector('.chat-bubble-row[data-message-id="' + tempId + '"]');
        tempRow.dataset.pendingKey = '1|' + text;
        afterRender(container);
        container.scrollTop = container.scrollHeight;
        updateComposerButton();
        patchListRow(conversationId, { lastMessagePreview: text, lastMessageDirection: 2, lastMessageAt: new Date().toISOString() });

        try {
            var message = await api('/api/conversations/' + conversationId + '/messages', {
                method: 'POST',
                body: JSON.stringify({ text: text })
            });
            if (!tempRow.isConnected) return; // already replaced by the realtime event, or chat switched
            if (container.querySelector('.chat-bubble-row[data-message-id="' + message.id + '"]')) tempRow.remove();
            else tempRow.outerHTML = renderBubble(message);
            afterRender(container);
        } catch (e) {
            if (!tempRow.isConnected) return;
            tempRow.outerHTML = renderBubble({
                id: tempId, direction: 2, status: 6, textBody: text, senderUserName: USER.name,
                whatsAppTimestamp: new Date().toISOString(),
                errorMessage: (e.body && e.body.error) || 'تعذر الاتصال بالخادم'
            });
        }
    }

    // ── Photos, files and voice notes ───────────────────────────────────────
    // type: 2 image, 3 video, 4 audio, 5 document (same numbers as the server)
    function mediaTypeFor(mime) {
        if (/^image\//.test(mime)) return 2;
        if (/^video\//.test(mime)) return 3;
        if (/^audio\//.test(mime)) return 4;
        return 5;
    }

    async function sendMedia(blob, fileName, caption) {
        var conversationId = state.selectedConversationId;
        if (!conversationId || !blob) return;

        var type = mediaTypeFor(blob.type);
        var container = document.getElementById('chat-messages');
        ensureTodaySeparator(container);

        var localUrl = URL.createObjectURL(blob);
        var tempId = 'pending-' + (++pendingSeq);
        var shownText = type === 5 ? (caption || fileName) : (caption || '');
        container.insertAdjacentHTML('beforeend', renderBubble({
            id: tempId, direction: 2, status: 2, messageType: type, textBody: shownText, localUrl: localUrl,
            senderUserName: USER.name, whatsAppTimestamp: new Date().toISOString()
        }));
        var tempRow = container.querySelector('.chat-bubble-row[data-message-id="' + tempId + '"]');
        tempRow.dataset.pendingKey = type + '|' + shownText;
        tempRow.dataset.localUrl = localUrl;
        afterRender(container);
        container.scrollTop = container.scrollHeight;
        patchListRow(conversationId, { lastMessagePreview: mediaPlaceholderText({ messageType: type }), lastMessageDirection: 2, lastMessageAt: new Date().toISOString() });

        var form = new FormData();
        form.append('file', blob, fileName);
        if (caption) form.append('caption', caption);

        try {
            var res = await fetch('/api/conversations/' + conversationId + '/media', { method: 'POST', body: form });
            var message = await res.json().catch(function () { return null; });
            if (!res.ok) throw { body: message };
            if (!tempRow.isConnected) return;
            if (container.querySelector('.chat-bubble-row[data-message-id="' + message.id + '"]')) tempRow.remove();
            else replacePending(tempRow, message);
            afterRender(container);
        } catch (e) {
            if (!tempRow.isConnected) return;
            tempRow.outerHTML = renderBubble({
                id: tempId, direction: 2, status: 6, messageType: type, textBody: shownText, localUrl: localUrl,
                senderUserName: USER.name, whatsAppTimestamp: new Date().toISOString(),
                errorMessage: (e.body && e.body.error) || 'تعذر رفع الملف'
            });
        }
    }

    // Phone photos are often 4–12 MB (WhatsApp allows 5): shrink to ≤1600px JPEG like WhatsApp does
    function compressImage(file) {
        return new Promise(function (resolve) {
            if (!/^image\/(jpeg|png|heic|heif|webp)$/i.test(file.type) && file.type !== '') { resolve(file); return; }
            var img = new Image();
            var url = URL.createObjectURL(file);
            img.onload = function () {
                var max = 1600, w = img.naturalWidth, h = img.naturalHeight;
                var scale = Math.min(1, max / Math.max(w, h));
                var canvas = document.createElement('canvas');
                canvas.width = Math.round(w * scale);
                canvas.height = Math.round(h * scale);
                canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height);
                URL.revokeObjectURL(url);
                canvas.toBlob(function (b) { resolve(b || file); }, 'image/jpeg', 0.82);
            };
            img.onerror = function () { URL.revokeObjectURL(url); resolve(file); };
            img.src = url;
        });
    }

    // Photo / video picked → WhatsApp-style preview with a caption box; documents go straight out
    var previewFile = null;
    function openMediaPreview(file) {
        var type = mediaTypeFor(file.type);
        if (type === 5) { sendMedia(file, file.name, null); return; }
        previewFile = file;
        var url = URL.createObjectURL(file);
        document.getElementById('media-preview-body').innerHTML = type === 3
            ? '<video src="' + url + '" controls playsinline></video>'
            : '<img src="' + url + '" alt="" />';
        document.getElementById('media-preview-caption').value = '';
        document.getElementById('media-preview').hidden = false;
    }

    function closeMediaPreview() {
        document.getElementById('media-preview').hidden = true;
        document.getElementById('media-preview-body').innerHTML = '';
        previewFile = null;
    }

    async function sendPreviewedMedia() {
        if (!previewFile) return;
        var file = previewFile;
        var caption = document.getElementById('media-preview-caption').value.trim();
        closeMediaPreview();
        if (mediaTypeFor(file.type) === 2) {
            var jpeg = await compressImage(file);
            sendMedia(jpeg, (file.name || 'photo').replace(/\.[^.]+$/, '') + '.jpg', caption);
        } else {
            sendMedia(file, file.name || 'video.mp4', caption);
        }
    }

    // ── Voice notes ─────────────────────────────────────────────────────────
    // WhatsApp only plays OGG/Opus as a real voice note. Browsers record WebM (Chrome) or MP4
    // (Safari), so we use opus-recorder (WASM Ogg/Opus encoder) and fall back to MediaRecorder.
    var OPUS_CDN = 'https://cdn.jsdelivr.net/npm/opus-recorder@8.0.5/dist/';
    var recorder = null, recordTimer = null, recordStart = 0, recordCancelled = false, opusReady = null;

    function loadScript(src) {
        return new Promise(function (resolve, reject) {
            var el = document.createElement('script');
            el.src = src; el.onload = resolve; el.onerror = reject;
            document.head.appendChild(el);
        });
    }

    function loadOpusRecorder() {
        if (!opusReady) {
            opusReady = Promise.all([
                window.Recorder ? Promise.resolve() : loadScript(OPUS_CDN + 'recorder.min.js'),
                // workers must be same-origin, so load the encoder as a blob URL
                fetch(OPUS_CDN + 'encoderWorker.min.js').then(function (r) { return r.text(); })
                    .then(function (code) { return URL.createObjectURL(new Blob([code], { type: 'application/javascript' })); })
            ]).then(function (results) { return results[1]; });
        }
        return opusReady;
    }

    function pickMediaRecorderType() {
        var types = ['audio/ogg;codecs=opus', 'audio/mp4', 'audio/aac', 'audio/mpeg'];
        if (typeof MediaRecorder === 'undefined') return null;
        for (var i = 0; i < types.length; i++) if (MediaRecorder.isTypeSupported(types[i])) return types[i];
        return null;
    }

    function showRecorderBar(on) {
        document.getElementById('chat-composer').hidden = on;
        document.getElementById('chat-recorder').hidden = !on;
        clearInterval(recordTimer);
        if (on) {
            recordStart = Date.now();
            var timeEl = document.getElementById('recorder-time');
            timeEl.textContent = '0:00';
            recordTimer = setInterval(function () { timeEl.textContent = formatDuration((Date.now() - recordStart) / 1000); }, 250);
        }
    }

    function finishRecording(blob, ext) {
        showRecorderBar(false);
        recorder = null;
        if (recordCancelled || !blob || blob.size < 1200) return; // cancelled, or a mis-tap
        sendMedia(blob, 'voice-' + Date.now() + '.' + ext, null);
    }

    async function startRecording() {
        if (recorder || !state.selectedConversationId) return;
        recordCancelled = false;
        try {
            var workerUrl = await loadOpusRecorder();
            if (!window.Recorder || !window.Recorder.isRecordingSupported()) throw new Error('opus unsupported');
            var rec = new window.Recorder({ encoderPath: workerUrl, numberOfChannels: 1, encoderSampleRate: 48000, streamPages: false, maxFramesPerPage: 40 });
            rec.ondataavailable = function (bytes) { finishRecording(new Blob([bytes], { type: 'audio/ogg' }), 'ogg'); };
            await rec.start();
            recorder = { stop: function () { rec.stop(); } };
            showRecorderBar(true);
            return;
        } catch (e) { /* fall back to the browser recorder below */ }

        var mime = pickMediaRecorderType();
        if (!mime) { alert('المتصفح ده مش بيدعم تسجيل رسائل صوتية لواتساب'); return; }
        try {
            var stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            var mr = new MediaRecorder(stream, { mimeType: mime });
            var chunks = [];
            mr.ondataavailable = function (ev) { if (ev.data.size) chunks.push(ev.data); };
            mr.onstop = function () {
                stream.getTracks().forEach(function (t) { t.stop(); });
                var baseType = mime.split(';')[0];
                finishRecording(new Blob(chunks, { type: baseType }), baseType === 'audio/ogg' ? 'ogg' : baseType === 'audio/mpeg' ? 'mp3' : 'm4a');
            };
            mr.start();
            recorder = { stop: function () { mr.stop(); } };
            showRecorderBar(true);
        } catch (err) {
            alert('لازم تسمح للموقع باستخدام الميكروفون عشان تسجل رسالة صوتية');
        }
    }

    function stopRecording(cancel) {
        if (!recorder) return;
        recordCancelled = !!cancel;
        recorder.stop();
        if (cancel) showRecorderBar(false);
    }

    function formatDuration(sec) {
        sec = Math.max(0, Math.floor(sec || 0));
        return Math.floor(sec / 60) + ':' + String(sec % 60).padStart(2, '0');
    }

    // Mic when the box is empty, send arrow when there's text — like WhatsApp
    function updateComposerButton() {
        var input = document.getElementById('composer-input');
        var btn = document.getElementById('composer-send-btn');
        var hasText = input.value.trim().length > 0;
        btn.classList.toggle('is-mic', !hasText);
        btn.innerHTML = hasText ? '<i class="bi bi-send-fill"></i>' : '<i class="bi bi-mic-fill"></i>';
        btn.title = hasText ? 'إرسال' : 'تسجيل رسالة صوتية';
    }

    // ── Voice-note player (one at a time, like WhatsApp) ────────────────────
    var activeAudio = null, activeNote = null;
    function toggleVoiceNote(note) {
        if (activeNote === note && activeAudio) {
            if (activeAudio.paused) activeAudio.play(); else activeAudio.pause();
            return;
        }
        if (activeAudio) { activeAudio.pause(); setNoteIcon(activeNote, false); }
        activeNote = note;
        activeAudio = new Audio(note.dataset.src);
        var progress = note.querySelector('.vn-progress'), knob = note.querySelector('.vn-knob'), time = note.querySelector('.vn-time');
        activeAudio.addEventListener('play', function () { setNoteIcon(note, true); });
        activeAudio.addEventListener('pause', function () { setNoteIcon(note, false); });
        activeAudio.addEventListener('loadedmetadata', function () { if (isFinite(activeAudio.duration)) time.textContent = formatDuration(activeAudio.duration); });
        activeAudio.addEventListener('timeupdate', function () {
            var d = activeAudio.duration;
            var pct = d && isFinite(d) ? (activeAudio.currentTime / d) * 100 : 0;
            progress.style.width = pct + '%';
            knob.style.insetInlineStart = pct + '%';
            time.textContent = formatDuration(activeAudio.currentTime);
        });
        activeAudio.addEventListener('ended', function () { setNoteIcon(note, false); progress.style.width = '0%'; knob.style.insetInlineStart = '0%'; });
        activeAudio.addEventListener('error', function () { time.textContent = 'تعذر التشغيل'; setNoteIcon(note, false); });
        activeAudio.play().catch(function () { /* autoplay blocked or failed — the error handler shows it */ });
    }

    function setNoteIcon(note, playing) {
        if (!note) return;
        note.querySelector('.vn-play').innerHTML = playing ? '<i class="bi bi-pause-fill"></i>' : '<i class="bi bi-play-fill"></i>';
    }

    function seekVoiceNote(note, clientX) {
        if (activeNote !== note || !activeAudio || !isFinite(activeAudio.duration)) return;
        var track = note.querySelector('.vn-track').getBoundingClientRect();
        var ratio = (clientX - track.left) / track.width;
        if (document.dir === 'rtl' || document.documentElement.dir === 'rtl') ratio = 1 - ratio;
        activeAudio.currentTime = Math.min(Math.max(ratio, 0), 1) * activeAudio.duration;
    }

    function autoGrow(el) {
        el.style.height = 'auto';
        el.style.height = Math.min(el.scrollHeight, 120) + 'px';
        // Only show a scrollbar once the text is taller than the max height
        el.style.overflowY = el.scrollHeight > 120 ? 'auto' : 'hidden';
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
            '<div id="cp-followups-list"></div>' +

            // Sticky at the bottom of the panel so it's reachable without scrolling back up
            '<div class="cp-save-bar">' +
            '  <div id="cp-save-msg" class="cp-save-msg"></div>' +
            '  <button id="cp-save-btn" class="btn btn-primary w-100" disabled><i class="bi bi-check-lg"></i> حفظ التغييرات</button>' +
            '</div>';

        // Agent / status / stage are edited together and saved with one button
        var saved = {
            agentId: c.assignedSalesAgentId || '',
            status: c.status,
            stage: c.leadStage,
            lostReason: (c.lostReason || '').trim()
        };
        var saveBtn = document.getElementById('cp-save-btn');
        var saveMsg = document.getElementById('cp-save-msg');
        var UNSAVED = 'لديك تغييرات غير محفوظة';

        function current() {
            var agentSelect = document.getElementById('cp-agent-select');
            return {
                agentId: agentSelect ? agentSelect.value : saved.agentId,
                status: Number(document.getElementById('cp-status-select').value),
                stage: Number(document.getElementById('cp-stage-select').value),
                lostReason: document.getElementById('cp-lost-reason').value.trim()
            };
        }
        function refreshDirty() {
            var now = current();
            var dirty = now.agentId !== saved.agentId || now.status !== saved.status || now.stage !== saved.stage ||
                (now.stage === 7 && now.lostReason !== saved.lostReason);
            saveBtn.disabled = !dirty;
            state.panelDirty = dirty;
            if (dirty) { saveMsg.className = 'cp-save-msg'; saveMsg.textContent = UNSAVED; }
            else if (saveMsg.textContent === UNSAVED) saveMsg.textContent = '';
        }

        if (USER.canAssign) populateAgentSelect(c.assignedSalesAgentId).then(refreshDirty);
        body.addEventListener('change', function (e) {
            if (e.target.id === 'cp-stage-select')
                document.getElementById('cp-lost-reason').style.display = Number(e.target.value) === 7 ? 'block' : 'none';
            if (['cp-agent-select', 'cp-status-select', 'cp-stage-select'].indexOf(e.target.id) !== -1) refreshDirty();
        });
        document.getElementById('cp-lost-reason').addEventListener('input', refreshDirty);

        saveBtn.addEventListener('click', async function () {
            var now = current();
            if (now.stage === 7 && !now.lostReason) {
                saveMsg.className = 'cp-save-msg error';
                saveMsg.textContent = 'اكتب سبب الخسارة قبل الحفظ';
                return;
            }
            saveBtn.disabled = true;
            saveBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span> جارٍ الحفظ...';
            saveMsg.textContent = '';
            try {
                if (now.agentId !== saved.agentId) {
                    await api('/api/conversations/' + c.id + '/assign', { method: 'POST', body: JSON.stringify({ agentId: now.agentId || null }) });
                    saved.agentId = now.agentId;
                    var agentSelect = document.getElementById('cp-agent-select');
                    var agentName = now.agentId ? agentSelect.options[agentSelect.selectedIndex].text : null;
                    document.getElementById('chat-header-sub').textContent = c.contactPhone + ' · ' + (agentName || 'غير مخصص');
                }
                if (now.status !== saved.status) {
                    await api('/api/conversations/' + c.id + '/status', { method: 'PATCH', body: JSON.stringify({ status: now.status }) });
                    saved.status = now.status;
                }
                if (now.stage !== saved.stage || (now.stage === 7 && now.lostReason !== saved.lostReason)) {
                    await api('/api/conversations/' + c.id + '/leadstage', {
                        method: 'PATCH',
                        body: JSON.stringify({ leadStage: now.stage, lostReason: now.stage === 7 ? now.lostReason : null })
                    });
                    saved.stage = now.stage;
                    saved.lostReason = now.lostReason;
                }
                state.panelSavedAt = Date.now();
                saveMsg.className = 'cp-save-msg success';
                saveMsg.innerHTML = '<i class="bi bi-check-circle-fill"></i> تم الحفظ';
                loadConversations();
            } catch (e) {
                saveMsg.className = 'cp-save-msg error';
                saveMsg.textContent = 'تعذر الحفظ' + (e.body && e.body.error ? ': ' + e.body.error : '');
            } finally {
                saveBtn.innerHTML = '<i class="bi bi-check-lg"></i> حفظ التغييرات';
                refreshDirty(); // leaves the success/error message in place
            }
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

        await getAgents();

        agentsCache.forEach(function (agent) {
            var opt = document.createElement('option');
            opt.value = agent.id;
            opt.textContent = agent.fullName;
            if (agent.id === currentAgentId) opt.selected = true;
            select.appendChild(opt);
        });
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
    var SCREENS = ['list', 'chat', 'customer'];
    var ignoreNextPop = false;

    function currentScreen() {
        var columns = document.getElementById('inbox-columns');
        return columns.classList.contains('screen-customer') ? 'customer'
             : columns.classList.contains('screen-chat') ? 'chat' : 'list';
    }

    function showScreen(name, fromHistory) {
        var columns = document.getElementById('inbox-columns');
        columns.classList.remove('screen-chat', 'screen-customer');
        if (name === 'chat') columns.classList.add('screen-chat');
        if (name === 'customer') columns.classList.add('screen-customer');
        document.body.classList.toggle('inbox-fullscreen', IS_MOBILE && name !== 'list');

        // Mirror the screen stack in browser history so the phone's back button goes back a screen
        if (!IS_MOBILE || fromHistory) return;
        var depth = SCREENS.indexOf(name);
        var current = (history.state && history.state.inboxDepth) || 0;
        if (depth > current) {
            history.pushState({ inboxDepth: depth }, '');
        } else if (depth < current) {
            ignoreNextPop = true; // the screen is already shown; just rewind history to match
            history.go(depth - current);
        }
    }

    window.addEventListener('popstate', function (e) {
        if (ignoreNextPop) { ignoreNextPop = false; return; }
        var depth = (e.state && e.state.inboxDepth) || 0;
        showScreen(SCREENS[depth] || 'list', true);
    });

    // Several realtime events arrive per message (new message, unread count, assignment…) and
    // each used to reload the chat list + account tabs. Coalesce a burst into one reload.
    var refreshTimer = null, pendingRefresh = { accounts: false, list: false };
    function scheduleRefresh(accounts, list) {
        pendingRefresh.accounts = pendingRefresh.accounts || accounts;
        pendingRefresh.list = pendingRefresh.list || list;
        clearTimeout(refreshTimer);
        refreshTimer = setTimeout(function () {
            var todo = pendingRefresh;
            pendingRefresh = { accounts: false, list: false };
            if (todo.accounts) loadAccounts();
            if (todo.list) loadConversations();
        }, 300);
    }

    // ── SignalR realtime ─────────────────────────────────────────────────────
    function connectSignalR() {
        if (typeof signalR === 'undefined') return;

        state.hub = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/inbox')
            .withAutomaticReconnect()
            .build();

        state.hub.on('NewMessage', function (payload) {
            if (payload.conversationId === state.selectedConversationId) {
                var container = document.getElementById('chat-messages');
                if (container) appendBubble(container, payload);
                if (payload.direction === 1) markRead(payload.conversationId);
            }
            scheduleRefresh(true, true);
        });

        state.hub.on('MessageStatusUpdated', function (payload) { updateBubbleStatus(payload); });

        state.hub.on('UnreadCountUpdated', function (payload) {
            var conv = state.conversations.find(function (c) { return c.id === payload.conversationId; });
            if (conv) { conv.unreadCount = payload.unreadCount; renderConversationList(); }
            scheduleRefresh(true, !conv);
        });

        state.hub.on('ConversationAssigned', function () { scheduleRefresh(false, true); });
        state.hub.on('ConversationUpdated', function (payload) {
            scheduleRefresh(false, true);
            // Re-render the open chat for changes made elsewhere — but not over the user's unsaved
            // edits, and not right after their own save (keeps the "saved" confirmation visible)
            var justSaved = Date.now() - (state.panelSavedAt || 0) < 4000;
            if (payload.conversationId === state.selectedConversationId && !state.panelDirty && !justSaved)
                selectConversation(payload.conversationId);
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
            setFilterChip(chip.dataset.filter);
            loadConversations();
        });

        document.getElementById('conv-search').addEventListener('input', function (e) {
            clearTimeout(state.searchDebounce);
            var value = e.target.value;
            state.searchDebounce = setTimeout(function () { state.searchTerm = value; loadConversations(); }, 300);
        });

        var input = document.getElementById('composer-input');
        // Phones have no Shift key and the long hint wraps — keep it short there
        if (IS_MOBILE) input.placeholder = 'اكتب رسالة...';
        input.addEventListener('input', function () { autoGrow(input); updateComposerButton(); });
        updateComposerButton();

        // attachments
        document.getElementById('composer-attach-btn').addEventListener('click', function () { document.getElementById('composer-file').click(); });
        document.getElementById('composer-camera-btn').addEventListener('click', function () { document.getElementById('composer-camera').click(); });
        ['composer-file', 'composer-camera'].forEach(function (id) {
            document.getElementById(id).addEventListener('change', function (e) {
                var file = e.target.files && e.target.files[0];
                e.target.value = '';
                if (file) openMediaPreview(file);
            });
        });
        document.getElementById('media-preview-cancel').addEventListener('click', closeMediaPreview);
        document.getElementById('media-preview-send').addEventListener('click', sendPreviewedMedia);
        document.getElementById('media-preview-caption').addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); sendPreviewedMedia(); }
        });

        // voice notes
        document.getElementById('recorder-cancel').addEventListener('click', function () { stopRecording(true); });
        document.getElementById('recorder-send').addEventListener('click', function () { stopRecording(false); });

        // voice-note players inside bubbles
        document.getElementById('chat-messages').addEventListener('click', function (e) {
            var note = e.target.closest('.voice-note');
            if (!note) return;
            if (e.target.closest('.vn-play')) toggleVoiceNote(note);
            else if (e.target.closest('.vn-track')) seekVoiceNote(note, e.clientX);
        });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });
        document.getElementById('composer-send-btn').addEventListener('click', function () {
            if (document.getElementById('composer-input').value.trim()) sendMessage();
            else startRecording();
        });

        document.getElementById('chat-messages').addEventListener('scroll', function (e) {
            if (e.target.scrollTop < 60) loadMessages(false);
        });

        // Lightweight fallback so the list/badges stay fresh even if SignalR can't connect
        // (e.g. behind a proxy that blocks WebSockets) — matches the existing 30s notif poll.
        setInterval(function () {
            if (state.hub && state.hub.state === 'Connected') return; // realtime already keeps it fresh
            loadAccounts();
            if (!state.selectedConversationId) loadConversations();
        }, 30000);

        window.addEventListener('resize', function () { IS_MOBILE = window.matchMedia('(max-width: 991px)').matches; });
    }

    function init() {
        if (!USER.isOrgWide) document.getElementById('inbox-app').classList.add('is-agent');
        // From the "deactivated — chats waiting" notification
        if (USER.canAssign && new URLSearchParams(location.search).get('f') === 'pending') state.agentFilter = 'unassigned';
        loadAccounts();
        loadConversations();
        bindEvents();
        initAgentFilter();
        connectSignalR();

        var linked = new URLSearchParams(location.search).get('c');
        if (linked) selectConversation(linked);
    }

    document.addEventListener('DOMContentLoaded', init);

    // ── Transfer a chat to another salesperson (admin / team leaders / head) ──
    async function openTransfer() {
        var c = state.selectedConversation;
        if (!c || !USER.canAssign) return;
        var agents = await getAgents();

        document.getElementById('transfer-current').innerHTML = 'المسؤول دلوقتي: <strong>' +
            esc(c.assignedSalesAgentName || 'غير مخصص') + '</strong>';
        var search = document.getElementById('transfer-search');
        search.value = '';

        // Group by team, the current holder left out
        var groups = {}, order = [];
        agents.forEach(function (a) {
            if (a.id === c.assignedSalesAgentId) return;
            var key = a.managerName ? 'فريق ' + a.managerName : 'بدون فريق';
            if (!groups[key]) { groups[key] = []; order.push(key); }
            groups[key].push(a);
        });
        order.sort(function (x, y) { return x === 'بدون فريق' ? 1 : y === 'بدون فريق' ? -1 : x.localeCompare(y, 'ar'); });

        var list = document.getElementById('transfer-list');
        list.innerHTML = order.map(function (key) {
            return '<div class="transfer-group">' + esc(key) + '</div>' + groups[key].map(function (a) {
                return '<button type="button" class="transfer-item" data-id="' + esc(a.id) + '" data-name="' + esc(a.fullName) + '">' +
                    '<span class="conv-avatar">' + initials(a.fullName) + '</span><span class="transfer-name">' + esc(a.fullName) + '</span>' +
                    '<i class="bi bi-chevron-left"></i></button>';
            }).join('');
        }).join('') || '<div class="text-muted text-center py-3">مفيش موظفين تانيين</div>';

        search.oninput = function () {
            var q = search.value.trim().toLowerCase();
            list.querySelectorAll('.transfer-item').forEach(function (b) {
                b.style.display = !q || b.dataset.name.toLowerCase().indexOf(q) !== -1 ? '' : 'none';
            });
        };
        // Two views in the popup: pick a person, then confirm
        var confirmBox = document.getElementById('transfer-confirm');
        var pickParts = [list, search, document.getElementById('transfer-current'), document.querySelector('#transferModal .transfer-note')];
        function showPick() { confirmBox.hidden = true; pickParts.forEach(function (el) { if (el) el.hidden = false; }); }
        showPick();

        list.onclick = function (e) {
            var item = e.target.closest('.transfer-item');
            if (!item || item.disabled) return;
            var customer = (c.contactName && c.contactName.replace(/[.\s]/g, '')) ? c.contactName : c.contactPhone;
            document.getElementById('tc-from').textContent = initials(c.assignedSalesAgentName || '؟');
            document.getElementById('tc-to').textContent = initials(item.dataset.name);
            document.getElementById('tc-text').innerHTML = 'تحويل محادثة <strong>' + esc(customer) + '</strong><br>من <strong>' +
                esc(c.assignedSalesAgentName || 'غير مخصص') + '</strong> إلى <strong>' + esc(item.dataset.name) + '</strong>؟';
            document.getElementById('tc-error').hidden = true;
            pickParts.forEach(function (el) { if (el) el.hidden = true; });
            confirmBox.hidden = false;
            document.getElementById('tc-back').onclick = showPick;
            document.getElementById('tc-ok').onclick = function () { doTransfer(item); };
        };

        async function doTransfer(item) {
            var okBtn = document.getElementById('tc-ok');
            okBtn.disabled = true;
            okBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span> جارٍ التحويل...';
            try {
                var res = await api('/api/conversations/' + c.id + '/assign', { method: 'POST', body: JSON.stringify({ agentId: item.dataset.id }) });
                modal.hide();
                if (state.selectedConversationId === c.id) {
                    var detail = await api('/api/conversations/' + c.id);
                    state.selectedConversation = detail;
                    state.panelDirty = false;
                    renderChatHeader(detail);
                    renderCustomerPanel(detail);
                }
                loadConversations();
                refreshPendingCount();
                showToast('تم تحويل المحادثة لـ ' + item.dataset.name + (res && res.leadMoved ? ' (ومعها ملف العميل)' : ''));
            } catch (err) {
                var box = document.getElementById('tc-error');
                box.textContent = 'تعذر التحويل' + (err.body && err.body.error ? ': ' + err.body.error : '');
                box.hidden = false;
            } finally {
                okBtn.disabled = false;
                okBtn.innerHTML = '<i class="bi bi-check-lg"></i> تأكيد التحويل';
            }
        }

        // Lift the popup out of the inbox layout so its full-screen mobile container can't clip or cover it
        var modalEl = document.getElementById('transferModal');
        if (modalEl.parentElement !== document.body) document.body.appendChild(modalEl);
        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();
        if (!IS_MOBILE) setTimeout(function () { search.focus(); }, 300);
    }

    // How many chats wait for someone to be assigned (after a salesperson was deactivated)
    async function refreshPendingCount() {
        var opt = document.getElementById('pending-option');
        if (!opt) return;
        try {
            var data = await api('/api/conversations?assigned=unassigned');
            opt.textContent = '⏳ في انتظار التعيين' + (data.totalCount ? ' (' + data.totalCount + ')' : '');
        } catch (e) { /* keep the plain label */ }
    }

    function showToast(text) {
        var t = document.getElementById('inbox-toast');
        if (!t) {
            t = document.createElement('div');
            t.id = 'inbox-toast';
            t.className = 'inbox-toast';
            document.body.appendChild(t);
        }
        t.textContent = text;
        t.classList.add('show');
        clearTimeout(t._timer);
        t._timer = setTimeout(function () { t.classList.remove('show'); }, 3500);
    }

    // ── Forward a message into another chat ─────────────────────────────────
    async function openForward(messageId) {
        var source = state.selectedConversation;
        if (!source) return;
        var modalEl = document.getElementById('forwardModal');
        if (modalEl.parentElement !== document.body) document.body.appendChild(modalEl);
        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        var list = document.getElementById('forward-list'), search = document.getElementById('forward-search');
        var confirmBox = document.getElementById('forward-confirm'), errorBox = document.getElementById('fc-error');
        var row = document.querySelector('.chat-bubble-row[data-message-id="' + messageId + '"] .chat-bubble');
        document.getElementById('forward-preview').innerHTML = row ? row.innerHTML : '';

        function showPick() { confirmBox.hidden = true; list.hidden = false; search.hidden = false; }
        showPick();
        search.value = '';

        async function load(term) {
            list.innerHTML = '<div class="text-muted text-center py-3"><span class="spinner-border spinner-border-sm"></span></div>';
            try {
                var data = await api('/api/conversations?' + new URLSearchParams(term ? { search: term } : {}).toString());
                var items = (data.items || []).filter(function (c) { return c.id !== source.id; });
                list.innerHTML = items.length ? items.map(function (c) {
                    var name = (c.contactName && c.contactName.replace(/[.\s]/g, '')) ? c.contactName : c.contactPhone;
                    return '<button type="button" class="transfer-item" data-id="' + esc(c.id) + '" data-name="' + esc(name) + '">' +
                        '<span class="conv-avatar">' + initials(name) + '</span>' +
                        '<span class="transfer-name">' + esc(name) + '<small class="d-block text-muted" dir="ltr" style="text-align:right">' + esc(c.contactPhone || '') + '</small></span>' +
                        '<i class="bi bi-chevron-left"></i></button>';
                }).join('') : '<div class="text-muted text-center py-3">مفيش محادثات</div>';
            } catch (e) { list.innerHTML = '<div class="text-danger text-center py-3">تعذر تحميل المحادثات</div>'; }
        }
        var debounce = null;
        search.oninput = function () { clearTimeout(debounce); debounce = setTimeout(function () { load(search.value.trim()); }, 300); };
        load('');

        list.onclick = function (e) {
            var item = e.target.closest('.transfer-item');
            if (!item) return;
            document.getElementById('fc-text').innerHTML = 'تحويل الرسالة دي لـ <strong>' + esc(item.dataset.name) + '</strong>؟';
            errorBox.hidden = true;
            list.hidden = true; search.hidden = true; confirmBox.hidden = false;
            document.getElementById('fc-back').onclick = showPick;
            var ok = document.getElementById('fc-ok');
            ok.onclick = async function () {
                ok.disabled = true;
                ok.innerHTML = '<span class="spinner-border spinner-border-sm"></span> جارٍ التحويل...';
                try {
                    await api('/api/conversations/' + item.dataset.id + '/forward', {
                        method: 'POST', body: JSON.stringify({ sourceConversationId: source.id, messageId: messageId })
                    });
                    modal.hide();
                    loadConversations();
                    showToast('تم تحويل الرسالة لـ ' + item.dataset.name);
                } catch (err) {
                    errorBox.textContent = (err.body && err.body.error) || 'تعذر التحويل';
                    errorBox.hidden = false;
                } finally {
                    ok.disabled = false;
                    ok.innerHTML = '<i class="bi bi-forward-fill"></i> تحويل';
                }
            };
        };
        modal.show();
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.bubble-forward-btn');
        if (btn) { e.stopPropagation(); openForward(btn.dataset.forward); }
    });

    window.Inbox = { showScreen: showScreen, openTransfer: openTransfer, openForward: openForward };
})();
