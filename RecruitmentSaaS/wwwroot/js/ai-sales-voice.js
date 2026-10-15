(function () {
    'use strict';
    const $ = id => document.getElementById(id);
    let chat = null, settings = null;
    async function api(path, method = 'GET', body) {
        const response = await fetch('/api/ai-sales-voice/' + path, { method,
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value },
            body: body ? JSON.stringify(body) : undefined });
        const result = await response.json().catch(() => null);
        if (!response.ok) {
            const errors = {
                'Review the transcript and confirm uncertain or critical details before approval.': 'صحّح النص وأكّد التحقق من التفاصيل غير الواضحة والأرقام والمواعيد قبل الاعتماد.',
                'Recording context changed or is not available for review.': 'تغيّر التحكم أو حالة التسجيل؛ حدّث المحادثة قبل المراجعة.',
                'An explicit retry confirmation and current failed recording are required.': 'أكّد الموافقة على إعادة المحاولة وحدّث حالة التسجيل أولًا.',
                'Voice processing requires its separate external-processing approval.': 'التفريغ الخارجي غير مفعّل في إعدادات الخادم.'
            };
            throw new Error(errors[result?.error] || result?.error || 'تعذر تنفيذ الإجراء.');
        }
        return result;
    }
    function p(parent, text) { const el = document.createElement('p'); el.textContent = text; parent.append(el); }
    async function guard(button, work) {
        button.disabled = true;
        try { await work(); } catch (error) { $('voice-status').textContent = error.message; }
        finally { button.disabled = false; }
    }
    async function load() {
        $('voice-candidates').replaceChildren(); $('voice-turns').replaceChildren();
        if (!chat) { $('voice-status').textContent = 'اختر محادثة لعرض التسجيلات.'; $('queue-voice').disabled = true; return; }
        const selected = chat.id;
        const [config, data] = await Promise.all([api('status'), api('conversations/' + selected)]);
        if (chat?.id !== selected) return;
        settings = config;
        $('voice-status').textContent = config.enabled && config.externalProcessingApproved ?
            'التفريغ متاح للتسجيلات الحديثة؛ يحتاج النص مراجعة قبل اقتراح الرد.' : 'التفريغ الخارجي غير مفعّل. يمكن عرض التسجيلات ومراجعتها عند تفعيل الخدمة.';
        $('queue-voice').disabled = !(config.enabled && config.externalProcessingApproved);
        data.candidates.forEach(m => {
            const label = document.createElement('label'); label.className = 'd-block';
            const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.value = m.id;
            label.append(checkbox, document.createTextNode(' تسجيل ' + new Date(m.whatsAppTimestamp).toLocaleString('ar-EG')));
            $('voice-candidates').append(label);
        });
        const statuses = ['في الانتظار', 'جاري التفريغ', 'يحتاج مراجعة', 'اعتمده الموظف', 'تعذر التفريغ', 'تغيّر التحكم؛ النص غير متاح للمساعد'];
        data.turns.forEach(t => {
            const card = document.createElement('article'); card.className = 'border rounded p-3 mb-2';
            p(card, statuses[t.status]);
            const parts = data.parts.filter(part => part.turnId === t.id);
            p(card, 'التسجيلات الأصلية: ' + parts.map(part => part.messageId).join('، '));
            p(card, 'مؤشر احتمالية أقل رمز: ' + (t.lowestTokenProbability === null ? 'غير متاح' : Math.round(t.lowestTokenProbability * 100) + '%') + ' — ليس قياسًا لدقة التفريغ.');
            if (t.errorCode) p(card, 'تعذر استخدام التسجيل (' + t.errorCode + ').');
            if (t.status === 2) {
                const text = document.createElement('textarea'); text.className = 'form-control mb-2'; text.maxLength = 4000; text.rows = 3;
                text.value = t.transcript; text.setAttribute('aria-label', 'النص المصحح للتسجيل'); card.append(text);
                const label = document.createElement('label'); label.className = 'd-block mb-2';
                const confirm = document.createElement('input'); confirm.type = 'checkbox';
                label.append(confirm, document.createTextNode(' تحققت من الأرقام والمواعيد والتفاصيل غير الواضحة مع العميل')); card.append(label);
                const review = document.createElement('button'); review.type = 'button'; review.className = 'btn btn-outline-primary'; review.textContent = 'اعتماد النص بعد المراجعة';
                review.addEventListener('click', () => guard(review, async () => {
                    if (chat?.id !== selected) return;
                    await api('turns/' + t.id + '/review', 'POST', { expectedVersion: t.version, reviewedText: text.value, criticalDetailsVerified: confirm.checked });
                    await load();
                })); card.append(review);
            } else p(card, t.reviewedText || t.transcript);
            if (t.status === 4 && settings.enabled && settings.externalProcessingApproved) {
                const label = document.createElement('label'); label.className = 'd-block'; const confirm = document.createElement('input'); confirm.type = 'checkbox';
                label.append(confirm, document.createTextNode(' أوافق على إعادة المحاولة؛ قد تتكرر تكلفة محاولة انقطعت')); card.append(label);
                const retry = document.createElement('button'); retry.type = 'button'; retry.className = 'btn btn-outline-secondary'; retry.textContent = 'إعادة محاولة الأجزاء المتبقية';
                retry.addEventListener('click', () => guard(retry, async () => {
                    if (chat?.id !== selected) return;
                    await api('turns/' + t.id + '/retry', 'POST', { expectedVersion: t.version, confirmProviderRetry: confirm.checked }); await load();
                })); card.append(retry);
            }
            $('voice-turns').append(card);
        });
    }
    $('queue-voice').addEventListener('click', () => guard($('queue-voice'), async () => {
        if (!chat) return;
        const messageIds = Array.from($('voice-candidates').querySelectorAll('input:checked')).map(el => el.value);
        if (!messageIds.length || messageIds.length > settings.maxPartsPerTurn) throw new Error('اختر من تسجيل واحد إلى ' + settings.maxPartsPerTurn + ' تسجيلات.');
        await api('conversations/' + chat.id + '/queue', 'POST', { expectedVersion: chat.control?.ownershipVersion ?? 0, messageIds }); await load();
    }));
    $('refresh-voice').addEventListener('click', () => guard($('refresh-voice'), load));
    window.addEventListener('ai-sales-conversation', event => { chat = event.detail; load().catch(error => { $('voice-status').textContent = error.message; }); });
})();
