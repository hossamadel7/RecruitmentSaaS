(function () {
    'use strict';
    const $ = id => document.getElementById(id);
    let chat = null, editing = null, policies = [];
    const keys = new Map();
    async function api(path, method = 'GET', body) {
        const headers = { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value };
        const signature = path + JSON.stringify(body);
        if (method !== 'GET') { if (!keys.has(signature)) keys.set(signature, crypto.randomUUID()); headers['Idempotency-Key'] = keys.get(signature); }
        const response = await fetch('/api/ai-office-bookings/' + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
        const result = await response.json().catch(() => null);
        if (!response.ok) throw new Error(result?.error || 'تعذر تنفيذ الإجراء.');
        if (method !== 'GET') keys.delete(signature); return result;
    }
    async function guard(button, work, status = 'booking-status') {
        button.disabled = true;
        try { await work(); } catch (error) { $(status).textContent = error.message; }
        finally { button.disabled = false; }
    }
    function p(parent, text) { const element = document.createElement('p'); element.textContent = text; parent.append(element); }
    function option(select, value, text) { const el = document.createElement('option'); el.value = value; el.textContent = text; select.append(el); }
    function reset() { editing = null; $('booking-form').reset(); $('save-booking').textContent = 'حفظ الحجز بعد التأكيد'; }
    async function load() {
        $('booking-list').replaceChildren(); $('booking-branch').replaceChildren(); $('booking-confirmation').replaceChildren();
        if (!chat) { $('booking-status').textContent = 'اختر محادثة.'; return; }
        const selected = chat.id; const [status, data] = await Promise.all([api('status'), api('conversations/' + selected)]);
        if (chat?.id !== selected) return;
        $('booking-status').textContent = status.enabled ? 'الحجز متاح بعد مراجعة تأكيد العميل؛ بحد أقصى ' + status.maxDaysAhead + ' يومًا.' : 'الحجوزات الجديدة وتغيير المواعيد غير مفعّلة؛ يمكن إلغاء حجز محفوظ.';
        option($('booking-branch'), '', 'اختر فرعًا بساعات معتمدة');
        data.policies.forEach(x => option($('booking-branch'), x.policy.branchId, x.branchName + ' — ' + minuteText(x.policy.opensAtMinute) + ' إلى ' + minuteText(x.policy.closesAtMinute)));
        option($('booking-confirmation'), '', 'اختر تأكيد العميل');
        data.evidence.forEach(m => option($('booking-confirmation'), m.id, new Date(m.createdAt).toLocaleString('ar-EG') + ' — ' + (m.textBody || 'تسجيل راجعه الموظف').slice(0, 150)));
        data.items.forEach(b => {
            const card = document.createElement('article'); card.className = 'border rounded p-3 mb-2';
            p(card, ['حجز محفوظ', 'ملغي', 'حضور موثق بالاستقبال'][b.status] + ' — ' + b.arrivalLocal.replace('T', ' ').slice(0, 16));
            p(card, b.purpose); p(card, b.verifiedVisitId ? 'دليل الحضور: ' + b.verifiedVisitId : 'الحضور غير موثق؛ لا يُصنّف العميل تلقائيًا كغير حاضر.');
            const history = data.history.filter(e => e.bookingId === b.id);
            history.forEach(e => p(card, new Date(e.at).toLocaleString('ar-EG') + ' — ' + ({ Confirmed: 'تأكيد', Rescheduled: 'تغيير موعد', Cancelled: 'إلغاء', VerifiedVisit: 'حضور موثق' }[e.action] || e.action)));
            if (b.status === 0) {
                const edit = document.createElement('button'); edit.type = 'button'; edit.className = 'btn btn-outline-primary me-2'; edit.textContent = 'تغيير الموعد بتأكيد جديد';
                edit.addEventListener('click', () => {
                    if (chat?.id !== selected) return;
                    reset(); editing = b; $('booking-branch').value = b.branchId; $('booking-arrival').value = b.arrivalLocal.slice(0, 16); $('booking-purpose').value = b.purpose;
                    $('save-booking').textContent = 'حفظ تغيير الموعد بعد التأكيد'; $('booking-status').textContent = 'اختر رسالة تأكيد جديدة من العميل.';
                }); card.append(edit);
                const reason = document.createElement('input'); reason.className = 'form-control my-2'; reason.maxLength = 500; reason.placeholder = 'سبب الإلغاء'; reason.setAttribute('aria-label', 'سبب إلغاء الاستشارة'); card.append(reason);
                const cancel = document.createElement('button'); cancel.type = 'button'; cancel.className = 'btn btn-outline-danger'; cancel.textContent = 'إلغاء الاستشارة';
                cancel.addEventListener('click', () => guard(cancel, async () => {
                    if (chat?.id !== selected) return;
                    if (!reason.value.trim()) throw new Error('اكتب سبب الإلغاء.');
                    await api(b.id + '/cancel', 'POST', { expectedOwnershipVersion: chat.control?.ownershipVersion ?? 0, expectedBookingVersion: b.version, reason: reason.value }); reset(); await load();
                })); card.append(cancel);
            }
            $('booking-list').append(card);
        });
    }
    const minuteText = value => String(Math.floor(value / 60)).padStart(2, '0') + ':' + String(value % 60).padStart(2, '0');
    const parseMinute = value => { const [hour, minute] = value.split(':').map(Number); return hour * 60 + minute; };
    function showPolicy() {
        const current = policies.find(p => p.branchId === $('policy-branch').value);
        $('policy-opens').value = current ? minuteText(current.opensAtMinute) : '';
        $('policy-closes').value = current ? minuteText(current.closesAtMinute) : '';
        $('policy-source').value = current?.sourceReference || ''; $('policy-until').value = current?.validUntil.slice(0, 10) || ''; $('policy-enabled').checked = current?.enabled || false;
        $('policy-days').querySelectorAll('input').forEach(c => { c.checked = !!((current?.openDaysMask || 0) & Number(c.value)); });
    }
    async function loadPolicies() {
        const account = $('account').value; if (!account) return;
        const [branches, items] = await Promise.all([api('branches'), api('policies?accountId=' + encodeURIComponent(account))]);
        if ($('account').value !== account) return;
        policies = items; const selected = $('policy-branch').value; $('policy-branch').replaceChildren();
        branches.forEach(b => option($('policy-branch'), b.id, b.name)); if (branches.some(b => b.id === selected)) $('policy-branch').value = selected; showPolicy();
    }
    ['الأحد', 'الإثنين', 'الثلاثاء', 'الأربعاء', 'الخميس', 'الجمعة', 'السبت'].forEach((day, i) => {
        const label = document.createElement('label'); const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.value = 1 << i;
        label.append(checkbox, document.createTextNode(' ' + day)); $('policy-days').append(label);
    });
    $('policy-branch').addEventListener('change', showPolicy);
    $('account').addEventListener('change', () => loadPolicies().catch(e => { $('policy-status').textContent = e.message; }));
    $('agent-select').addEventListener('change', () => loadPolicies().catch(e => { $('policy-status').textContent = e.message; }));
    window.addEventListener('ai-sales-conversation', event => { chat = event.detail; reset(); load().catch(e => { $('booking-status').textContent = e.message; }); loadPolicies().catch(e => { $('policy-status').textContent = e.message; }); });
    $('booking-form').addEventListener('submit', event => { event.preventDefault(); guard($('save-booking'), async () => {
        if (!chat?.control) throw new Error('اختر محادثة لها سجل تحكم.');
        const selected = chat.id;
        await api(editing ? editing.id + '/reschedule' : 'conversations/' + selected, 'POST', {
            expectedOwnershipVersion: chat.control.ownershipVersion, expectedBookingVersion: editing?.version || 0,
            branchId: $('booking-branch').value, arrivalLocal: $('booking-arrival').value + ':00', purpose: $('booking-purpose').value,
            confirmationMessageId: $('booking-confirmation').value, customerConfirmed: $('booking-customer-confirmed').checked, generalConsultationDisclosed: $('booking-disclosed').checked
        }); if (chat?.id !== selected) return; reset(); await load();
    }); });
    $('reset-booking').addEventListener('click', reset); $('refresh-bookings').addEventListener('click', () => guard($('refresh-bookings'), load));
    $('office-policy-form').addEventListener('submit', event => { event.preventDefault(); guard($('office-policy-form').querySelector('button'), async () => {
        const branchId = $('policy-branch').value;
        const mask = Array.from($('policy-days').querySelectorAll('input:checked')).reduce((sum, c) => sum | Number(c.value), 0);
        await api('policies', 'POST', { expectedVersion: policies.find(p => p.branchId === branchId)?.version || 0,
            whatsAppAccountId: $('account').value, branchId, openDaysMask: mask, opensAtMinute: parseMinute($('policy-opens').value), closesAtMinute: parseMinute($('policy-closes').value),
            validUntil: new Date($('policy-until').value + 'T23:59:59').toISOString(), sourceReference: $('policy-source').value, enabled: $('policy-enabled').checked });
        await loadPolicies(); await load(); $('policy-status').textContent = 'تم اعتماد ساعات المكتب.';
    }, 'policy-status'); });
})();
