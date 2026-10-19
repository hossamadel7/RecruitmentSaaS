(function () {
    'use strict';
    const $ = id => document.getElementById(id), keys = new Map();
    let chat = null;
    let sending = false;
    const messages = {
        'A new customer message is required before drafting.': 'ننتظر رسالة جديدة من العميل قبل اقتراح رد.',
        'This conversation is outside the approved draft pilot.': 'اقتراح الردود غير مفعّل لهذه المحادثة حالياً.',
        'Reply drafts are disabled.': 'اقتراح الردود متوقف حالياً.',
        'Drafts require an explicitly assigned, resumed shadow conversation after intake.': 'عيّن العميل للمساعد واستأنف المتابعة أولاً.',
        'Control changed; refresh first.': 'تغيّرت حالة المحادثة. أعد اختيار العميل لتحديثها.',
        'Customer reply window closed or invalid. Use the staff template workflow.': 'انتهت مدة الرد على واتساب. افتح المحادثة واستخدم قالباً معتمداً.',
        'A current, reviewed reply without a human escalation is required.': 'هذا الرد يحتاج متابعة موظف أو تغيّرت المحادثة. راجع المحادثة أولاً.',
        'Reviewed dispatch has not been released for this account.': 'الإرسال غير مفعّل لهذه المحادثة.',
        'Daily dispatch attempt limit reached.': 'وصلنا لحد الإرسال اليومي لهذه التجربة.'
    };
    async function api(path, method = 'GET', body, area = 'ai-sales-engine') {
        const signature = method + area + path + JSON.stringify(body);
        const headers = { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value };
        if (method !== 'GET') {
            if (!keys.has(signature)) keys.set(signature, crypto.randomUUID());
            headers['Idempotency-Key'] = keys.get(signature);
        }
        const response = await fetch('/api/' + area + '/' + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
        if (response.redirected) throw new Error('سجّل الدخول مرة أخرى ثم حدّث الصفحة.');
        const result = await response.json();
        if (!response.ok) throw new Error(messages[result.error] || result.error || 'تعذر تنفيذ الإجراء.');
        if (method === 'POST' && path.endsWith('/drafts')) keys.delete(signature);
        return result;
    }
    function text(parent, value, className) {
        const element = document.createElement('p'); element.textContent = value;
        if (className) element.className = className;
        parent.append(element); return element;
    }
    async function loadDraft() {
        if (!chat) return;
        const selected = chat.id;
        const [rows, dispatchStatus, records] = await Promise.all([
            api('conversations/' + selected + '/drafts'),
            api('status?conversationId=' + selected, 'GET', undefined, 'ai-sales-dispatch'),
            api('conversations/' + selected, 'GET', undefined, 'ai-sales-dispatch')
        ]);
        if (chat?.id !== selected) return;
        sending = dispatchStatus.sendingAvailable;
        if (dispatchStatus.automaticDispatch) $('generate-draft').disabled = true;
        $('sending-status').textContent = dispatchStatus.automaticDispatch
            ? 'الرد التلقائي مفعّل لهذه المحادثة. تابع الرسائل من فتح المحادثة، واستلمها بشريًا لإيقاف المساعد.'
            : sending ? 'بعد المراجعة، اضغط اعتماد وإرسال لإرسال الرد للعميل.' : 'راجع الرد قبل اعتماده. الإرسال لهذه المحادثة متوقف حالياً.';
        $('draft-list').replaceChildren();
        if (!rows.length) { text($('draft-list'), 'لا يوجد رد مقترح بعد.', 'text-muted'); return; }
        const { draft, current, sources } = rows[0];
        const record = records.find(r => r.draftId === draft.id);
        const card = document.createElement('article'); card.className = 'border rounded p-3';
        const labels = ['جاري تجهيز الرد', 'جاهز للمراجعة', 'تم اعتماد الرد — لم يُرسل', 'تم رفض الرد', 'تغيّرت المحادثة؛ اقترح رداً جديداً', 'تعذر تجهيز الرد', 'رد تلقائي من المساعد'];
        const dispatchLabels = { Sent: 'تم إرسال الرد', Sending: 'الإرسال قيد التحقق — لا تعِد الإرسال', Unknown: 'نتيجة الإرسال غير مؤكدة. راجع المحادثة قبل أي إرسال آخر.', Queued: 'الرد معتمد وجاهز للإرسال', Cancelled: 'تم إلغاء الإرسال', Invalidated: 'تغيّرت المحادثة؛ اقترح رداً جديداً' };
        text(card, record ? dispatchLabels[record.state] || 'راجع حالة الإرسال' : labels[draft.status] || 'حالة غير معروفة', 'small text-muted');
        if (draft.reply) text(card, draft.reply).style.whiteSpace = 'pre-wrap';
        if (draft.needsHuman) text(card, 'هذا الطلب يحتاج متابعة موظف.', 'small text-warning');
        if (draft.errorCode) text(card, 'تعذر تجهيز الرد. حاول مجدداً أو تابع العميل بنفسك.', 'text-danger');
        if (draft.status === 1 && !current) text(card, 'هذا الرد لم يعد مناسباً لحالة المحادثة الحالية.', 'small text-warning');
        if (sources?.length) {
            const details = document.createElement('details'), summary = document.createElement('summary');
            summary.textContent = 'مصادر الرد'; details.className = 'small mb-3'; details.append(summary);
            sources.forEach(source => text(details, source.title)); card.append(details);
        }
        if (current && !record && draft.status === 1) {
            [true, false].forEach(accept => {
                const button = document.createElement('button'); button.type = 'button';
                button.className = 'btn ' + (accept ? 'btn-primary' : 'btn-outline-secondary') + ' me-2';
                const send = accept && sending && !draft.needsHuman;
                button.textContent = accept ? (send ? 'اعتماد وإرسال' : 'اعتماد الرد') : 'رفض الرد';
                button.addEventListener('click', async () => {
                    button.disabled = true;
                    try {
                        await api('drafts/' + draft.id + '/review', 'POST', { accept });
                        if (chat?.id !== selected) return;
                        if (send) { await sendReviewed(draft, selected); return; }
                        await loadDraft(); $('engine-status').textContent = accept ? 'تم اعتماد الرد فقط؛ لم يُرسل.' : 'تم رفض الرد.';
                    } catch (error) { if (chat?.id === selected) { await loadDraft().catch(() => {}); $('engine-status').textContent = error.message; } }
                    finally { button.disabled = false; }
                });
                card.append(button);
            });
        }
        if (draft.status === 2 && current && sending && !draft.needsHuman && (!record || record.state === 'Queued')) {
            const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-primary';
            button.textContent = 'إرسال الرد المعتمد';
            button.addEventListener('click', async () => {
                button.disabled = true;
                try { await sendReviewed(draft, selected, record); }
                catch (error) { if (chat?.id === selected) { await loadDraft().catch(() => {}); $('engine-status').textContent = error.message; } }
                finally { button.disabled = false; }
            });
            card.append(button);
        }
        $('draft-list').append(card);
    }
    async function sendReviewed(draft, selected, existing) {
        if (chat?.id !== selected) return;
        $('engine-status').textContent = 'جاري إرسال الرد…';
        const intent = existing || await api('', 'POST', { draftId: draft.id, expectedOwnershipVersion: draft.ownershipVersion }, 'ai-sales-dispatch');
        if (chat?.id !== selected) return;
        const result = await api(intent.id + '/send', 'POST', undefined, 'ai-sales-dispatch');
        if (chat?.id !== selected) return;
        await loadDraft();
        $('engine-status').textContent = result.state === 'Sent' ? 'تم إرسال الرد للعميل.'
            : result.state === 'Invalidated' ? 'تغيّرت المحادثة؛ لم يُرسل الرد. اقترح رداً جديداً.'
            : 'لم يتم تأكيد الإرسال. افتح المحادثة وراجعها؛ لا تعِد إرسال الرد تلقائياً.';
    }
    window.addEventListener('ai-sales-conversation', async event => {
        chat = event.detail; sending = false; $('draft-list').replaceChildren(); $('generate-draft').disabled = true;
        if (!chat) { $('engine-status').textContent = 'اختار محادثة لعرض الرد.'; return; }
        const selected = chat.id;
        try {
            const status = await api('status');
            if (chat?.id !== selected) return;
            $('generate-draft').disabled = !status.draftsEnabled || chat.control?.controller !== 2;
            $('engine-status').textContent = !chat.assignedAiSalesAgentId ? 'عيّن العميل للمساعد لبدء المتابعة.'
                : chat.control?.controller !== 2 ? 'المتابعة متوقفة. يمكنك استئنافها من إدارة المتابعة.'
                : !status.draftsEnabled ? 'اقتراح الردود متوقف حالياً.' : 'يمكنك طلب رد على أحدث رسالة من العميل.';
            await loadDraft();
        } catch (error) { if (chat?.id === selected) $('engine-status').textContent = error.message; }
    });
    $('generate-draft').addEventListener('click', async () => {
        if (!chat?.control) return;
        const selected = chat.id, button = $('generate-draft'); button.disabled = true;
        $('engine-status').textContent = 'جاري تجهيز الرد…';
        try {
            await api('conversations/' + selected + '/drafts', 'POST', { expectedVersion: chat.control.ownershipVersion });
            if (chat?.id !== selected) return;
            await loadDraft(); $('engine-status').textContent = 'راجع الرد قبل اعتماده.';
        } catch (error) { if (chat?.id === selected) $('engine-status').textContent = error.message; }
        finally { if (chat?.id === selected) button.disabled = false; }
    });
    setInterval(() => { if (chat && !document.hidden) loadDraft().catch(() => {}); }, 15000);
}());
