(function () {
    'use strict';
    const $ = id => document.getElementById(id);
    let agents = [], accounts = [], chats = [], current = null;
    const requestKeys = new Map();
    // Assignment controls are far below the page header on phones. Keep their
    // validation/progress/result beside the clicked button instead of offscreen.
    const assignmentStatus = document.createElement('p');
    assignmentStatus.id = 'assignment-status';
    assignmentStatus.setAttribute('role', 'status');
    assignmentStatus.setAttribute('aria-live', 'polite');
    const assignButton = document.querySelector('[data-action="assign"]');
    assignButton.parentElement.before(assignmentStatus);
    async function api(path, method = 'GET', body) {
        const headers = { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value };
        const signature = method + path + JSON.stringify(body);
        if (method !== 'GET') {
            if (!requestKeys.has(signature)) requestKeys.set(signature, crypto.randomUUID());
            headers['Idempotency-Key'] = requestKeys.get(signature);
        }
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 20000);
        let response;
        try {
            response = await fetch('/api/ai-sales/' + path, { method, headers, body: body ? JSON.stringify(body) : undefined, signal: controller.signal });
        } catch (e) {
            if (e.name === 'AbortError') throw new Error('لم يتم تأكيد الحفظ بسبب تأخر الاتصال. حدّث الصفحة وتحقق من حالة المحادثة قبل إعادة المحاولة.');
            throw new Error('تعذر الاتصال بالخادم. تحقق من الإنترنت ثم حدّث حالة المحادثة.');
        } finally { clearTimeout(timeout); }
        if (response.redirected && new URL(response.url).pathname.toLowerCase().includes('/auth/login'))
            throw new Error('انتهت جلسة الدخول. سجّل الدخول مرة أخرى ثم أعد المحاولة.');
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.error || 'تعذر تنفيذ الإجراء. حدّث الصفحة وتحقق من الصلاحيات.');
        return result;
    }
    function option(select, value, label) { const o = document.createElement('option'); o.value = value; o.textContent = label; select.append(o); }
    async function loadAgents() {
        const old = $('agent-select').value;
        const data = await api('agents'); agents = data.agents;
        $('ai-status').textContent = data.enabled ? '' : 'تعيين المساعد غير متاح حالياً.';
        fillAgents(null, old);
    }
    function fillAgents(accountId, selected) {
        $('agent-select').replaceChildren(); option($('agent-select'), '', 'اختار المساعد');
        agents.filter(a => !accountId || a.whatsAppAccountId === accountId).forEach(a =>
            option($('agent-select'), a.id, (accounts.find(x => x.id === a.whatsAppAccountId)?.name || '') + ' — ' + a.displayName + (a.paused ? ' (متوقف)' : '')));
        $('agent-select').value = selected || '';
        $('agent-select').dispatchEvent(new Event('change'));
    }
    async function search() {
        chats = await api('candidates?search=' + encodeURIComponent($('search').value) + ($('search-account').value ? '&accountId=' + encodeURIComponent($('search-account').value) : ''));
        $('chat-select').replaceChildren(); chats.forEach(c => option($('chat-select'), c.conversationId, (c.accountName || '') + ' — ' + (c.leadCode || '') + ' — ' + c.name + (c.assignedAiSalesAgentId ? ' — مساعد افتراضي' : '')));
        const requested = new URLSearchParams(location.search).get('conversationId');
        if (chats.some(c => c.conversationId === requested)) $('chat-select').value = requested;
        await loadControl();
    }
    async function loadControl() {
        const id = $('chat-select').value;
        current = null; $('control-state').textContent = '';
        window.dispatchEvent(new CustomEvent('ai-sales-conversation', { detail: null }));
        assignmentStatus.textContent = ''; assignmentStatus.className = '';
        if (!id) { fillAgents(null, ''); $('control-state').textContent = 'لا توجد محادثات مطابقة للبحث.'; window.dispatchEvent(new CustomEvent('ai-sales-conversation', { detail: null })); return; }
        const loaded = await api('conversations/' + id);
        if ($('chat-select').value !== id) return;
        current = loaded;
        const selectedAgent = agents.find(a => a.id === $('agent-select').value);
        const matching = agents.filter(a => a.whatsAppAccountId === current.whatsAppAccountId);
        fillAgents(current.whatsAppAccountId, current.assignedAiSalesAgentId ||
            (selectedAgent?.whatsAppAccountId === current.whatsAppAccountId ? selectedAgent.id : matching.length === 1 ? matching[0].id : ''));
        $('open-chat').href = '/Inbox/Index?c=' + encodeURIComponent(id);
        const names = ['يتابع العميل موظف', 'جمع بيانات العميل', 'المساعد يتابع المحادثة', 'متابعة المساعد متوقفة'];
        $('control-state').textContent = current.control ? names[current.control.controller] : 'لم يُعيّن العميل للمساعد بعد.';
        assignButton.disabled = !!current.assignedAiSalesAgentId;
        window.dispatchEvent(new CustomEvent('ai-sales-conversation', { detail: current }));
    }
    async function guarded(work, status = $('ai-status')) {
        document.querySelectorAll('#ai-sales-page [data-action], #search-form button').forEach(b => b.disabled = true);
        if (status === assignmentStatus) {
            status.className = 'alert alert-info';
            status.textContent = 'جاري حفظ الإجراء…';
        }
        try { await work(); } catch (e) {
            $('ai-status').textContent = e.message;
            status.className = 'alert alert-danger';
            status.textContent = e.message;
            status.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
        finally {
            document.querySelectorAll('#ai-sales-page [data-action], #search-form button').forEach(b => b.disabled = false);
            assignButton.disabled = !!current?.assignedAiSalesAgentId;
        }
    }
    $('agent-select').addEventListener('change', () => {
        const a = agents.find(x => x.id === $('agent-select').value);
        $('agent-state').textContent = a ? (a.paused ? 'المساعد متوقف: التعيين لا يبدأ تجهيز ردود أو إرسال رسائل، ويوقف جمع البيانات الجاري حتى الاستئناف أو الاستلام البشري.' : 'المساعد جاهز للمتابعة.') : '';
    });
    $('search-form').addEventListener('submit', e => { e.preventDefault(); guarded(search); });
    $('search-account').addEventListener('change', () => guarded(search));
    $('chat-select').addEventListener('change', () => guarded(loadControl));
    document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => guarded(async () => {
        if (!current) throw new Error('اختار المحادثة أولاً.');
        const action = button.dataset.action, version = current.control?.ownershipVersion || 0;
        const reasons = { assign: 'تعيين يدوي بواسطة الإدارة', takeover: 'استلام بشري بواسطة الإدارة', resume: 'استئناف بواسطة الإدارة', release: 'إعادة لمسار الموظفين بواسطة الإدارة' };
        const reason = $('reason').value.trim() || reasons[action];
        if (action === 'assign') {
            if (!$('agent-select').value) throw new Error('اختار مساعداً محفوظاً.');
            if (agents.find(a => a.id === $('agent-select').value)?.whatsAppAccountId !== current.whatsAppAccountId)
                throw new Error('اختار مساعد نفس رقم واتساب الخاص بالعميل.');
            await api('leads/' + current.leadId + '/assign', 'POST', { agentId: $('agent-select').value, conversationId: current.id, expectedVersion: version, reason, conversationOnly: true });
        } else await api('conversations/' + current.id + '/' + action, 'POST', { expectedVersion: version, reason });
        await loadControl(); $('ai-status').textContent = 'تم حفظ الإجراء.';
        assignmentStatus.className = 'alert alert-success';
        assignmentStatus.textContent = action === 'assign'
            ? (current.control?.controller === 3 ? 'تم تعيين العميل للمساعد بنجاح — المحادثة متوقفة، ولم يبدأ تجهيز ردود أو إرسال رسائل.' : 'تم تعيين العميل للمساعد بنجاح.')
            : 'تم حفظ الإجراء.';
        assignmentStatus.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }, assignmentStatus)));
    guarded(async () => { accounts = await api('setup'); accounts.forEach(a => option($('search-account'), a.id, a.name)); await loadAgents(); await search(); });
}());
