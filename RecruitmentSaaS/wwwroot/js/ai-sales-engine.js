(function () {
    'use strict';
    const $ = id => document.getElementById(id);
    let chat = null, memory = null;
    let improvementSource = null;
    function showMemory(value) {
        const details = JSON.parse(value?.reviewedDetailsJson || '{}');
        $('memory-summary').value = value?.reviewedSummary || '';
        $('memory-passport').value = String(details.passportReadiness || 0);
        $('memory-work').value = details.preferredWork || '';
        $('memory-objection').value = details.mainObjection || '';
        $('memory-next-step').value = details.nextStep || '';
        $('memory-visit').value = details.visitPreference || '';
    }
    const keys = new Map();
    function resetImprovement() {
        improvementSource = null;
        $('improvement-source').textContent = 'اختار حالة من القائمة لاقتراح تحسين.';
        $('improvement-form').reset();
    }
    async function loadImprovements() {
        resetImprovement(); $('improvement-cases').replaceChildren();
        const account = $('account').value;
        if (!account) throw new Error('اختار رقم واتساب.');
        const rows = await api('improvements?accountId=' + encodeURIComponent(account));
        if ($('account').value !== account) return;
        const outcomes = { VerifiedVisit: 'حضور موثق مرتبط بالمحادثة', BookedAttendanceUnknown: 'حجز موجود؛ الحضور غير معروف',
            CancelledAttendanceUnknown: 'حجز ملغي؛ الحضور غير معروف', Unknown: 'النتيجة غير معروفة' };
        for (const row of rows) {
            const card = document.createElement('article'); card.className = 'border rounded p-3 my-2';
            const title = document.createElement('h3'); title.className = 'h6';
            title.textContent = 'حالة ' + row.draftId.slice(0, 8) + ' — ' + ({2:'راجعه الموظف وقبله',3:'رفضه الموظف',5:'تعذر إعداد المسودة'}[row.status] || 'للمراجعة');
            const reply = document.createElement('p'); reply.style.whiteSpace = 'pre-wrap'; reply.textContent = row.reply || 'لا يوجد رد محفوظ.';
            const evidence = document.createElement('p'); evidence.textContent = outcomes[row.outcomeEvidence] || 'النتيجة غير معروفة';
            const note = document.createElement('p'); note.textContent = row.reviewNote || '';
            const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-primary'; button.textContent = 'اقتراح تحسين لهذه الحالة';
            button.addEventListener('click', () => {
                if ($('account').value !== account) return;
                resetImprovement(); improvementSource = { row, account };
                $('improvement-source').textContent = 'اقتراح تحسين للحالة ' + row.draftId.slice(0, 8);
                $('improvement-title').focus();
            });
            card.append(title, reply, evidence, note, button); $('improvement-cases').append(card);
        }
        $('improvement-status').textContent = rows.length ? 'راجع الحالة والنتيجة، ثم اكتب اقتراحك بدون بيانات شخصية.' : 'لا توجد حالات راجعها الموظف أو مسودات متعذرة لهذا الرقم.';
    }
    async function api(path, method = 'GET', body) {
        const headers = { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value };
        if (method !== 'GET') {
            const signature = method + path + JSON.stringify(body);
            if (!keys.has(signature)) keys.set(signature, crypto.randomUUID());
            headers['Idempotency-Key'] = keys.get(signature);
        }
        const response = await fetch('/api/ai-sales-engine/' + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
        const result = await response.json().catch(() => null);
        if (!response.ok) throw new Error(result?.error || 'تعذر تنفيذ الإجراء.');
        // A deliberate new generation gets a new key; uncertain network retries reuse the old key.
        if (method === 'POST' && path.endsWith('/drafts')) keys.delete(method + path + JSON.stringify(body));
        return result;
    }
    async function guard(button, work, status = 'engine-status') {
        button.disabled = true;
        try { await work(); } catch (error) { $(status).textContent = error.message; }
        finally { button.disabled = false; }
    }
    function paragraph(parent, text) { const p = document.createElement('p'); p.textContent = text; parent.append(p); }
    async function loadDrafts() {
        if (!chat) return;
        const selected = chat.id;
        const items = await api('conversations/' + selected + '/drafts');
        if (chat?.id !== selected) return;
        $('draft-list').replaceChildren();
        const statuses = ['جاري الإعداد', 'تحتاج مراجعة', 'راجعها الموظف', 'مرفوضة', 'قديمة بسبب تغيّر السياق', 'تعذر إعدادها'];
        items.forEach(({ draft: d, sources, current }) => {
            const card = document.createElement('article'); card.className = 'border rounded p-3 mb-2';
            paragraph(card, statuses[d.status] + (current ? '' : ' — غير متاحة للاستخدام'));
            const errors = { ExternalProcessingDisabled: 'استخدام مزود الذكاء الاصطناعي الخارجي غير مفعّل.', ProviderNotConfigured: 'إعدادات مزود الذكاء الاصطناعي غير مكتملة.', ContextChanged: 'تغيّرت المحادثة؛ اقترح مسودة جديدة.', InterruptedGeneration: 'انقطع إعداد المسودة؛ اقترح مسودة جديدة.' };
            paragraph(card, d.reply || errors[d.errorCode] || (d.errorCode ? 'تعذر إعداد المسودة؛ راجع إعدادات الخدمة أو حاول لاحقًا.' : '')); paragraph(card, d.reviewNote);
            paragraph(card, 'مراجع المعلومات: ' + sources.map(s => s.title + ' (' + s.sourceReference + ')').join('، '));
            if (d.inputTokens !== null || d.outputTokens !== null) paragraph(card, 'استخدام النموذج: ' + (d.inputTokens ?? '؟') + ' رموز إدخال، ' + (d.outputTokens ?? '؟') + ' رموز إخراج.');
            if (d.status === 1 && current) {
                [true, false].forEach(accept => {
                    const b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-outline-primary me-2';
                    b.textContent = accept ? 'تمت مراجعة المسودة' : 'رفض المسودة';
                    b.addEventListener('click', () => guard(b, async () => { await api('drafts/' + d.id + '/review', 'POST', { accept }); await loadDrafts(); }));
                    card.append(b);
                });
            }
            $('draft-list').append(card);
        });
    }
    async function loadKnowledge() {
        const account = $('account').value;
        if (!account) return;
        const items = await api('knowledge?accountId=' + encodeURIComponent(account));
        if ($('account').value !== account) return;
        $('knowledge-list').replaceChildren();
        items.forEach(k => {
            const card = document.createElement('article'); card.className = 'border rounded p-3 mb-2';
            paragraph(card, k.title + ' — ' + ['تحتاج اعتمادًا', 'معتمدة', 'متوقفة'][k.status]);
            paragraph(card, k.content); paragraph(card, 'مرجع: ' + k.sourceReference + ' — حتى ' + new Date(k.validUntil).toLocaleDateString('ar-EG'));
            const actions = k.status === 0 ? ['approve', 'retire'] : k.status === 1 ? ['retire'] : [];
            actions.forEach(action => {
                const b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-outline-primary me-2';
                b.textContent = action === 'approve' ? 'اعتماد بعد التحقق' : 'إيقاف المعلومة';
                b.addEventListener('click', () => guard(b, async () => { await api('knowledge/' + k.id + '/' + action, 'POST', { expectedVersion: k.version }); await loadKnowledge(); await loadDrafts(); }, 'knowledge-status'));
                card.append(b);
            });
            $('knowledge-list').append(card);
        });
    }
    window.addEventListener('ai-sales-conversation', async event => {
        chat = event.detail;
        memory = null; showMemory(null);
        if (!chat) { $('draft-list').replaceChildren(); $('engine-status').textContent = 'اختار محادثة.'; return; }
        const selected = chat.id;
        try {
            const data = await api('conversations/' + selected + '/memory');
            if (chat?.id !== selected) return;
            memory = data; showMemory(memory);
            const status = await api('status');
            $('engine-status').textContent = status.draftsEnabled ? 'المسودات مفعلة للمراجعة البشرية. الحد اليومي: ' + status.maxDraftsPerDay : 'إعداد المسودات معطل في إعدادات الخادم.';
            await loadDrafts(); await loadKnowledge();
        } catch (error) { $('engine-status').textContent = error.message; }
    });
    $('account').addEventListener('change', () => { resetImprovement(); $('improvement-cases').replaceChildren(); loadKnowledge().catch(e => $('knowledge-status').textContent = e.message); });
    $('agent-select').addEventListener('change', () => loadKnowledge().catch(e => $('knowledge-status').textContent = e.message));
    $('save-memory').addEventListener('click', () => guard($('save-memory'), async () => {
        if (!chat) throw new Error('اختار محادثة.');
        const selected = chat.id;
        const saved = await api('conversations/' + selected + '/memory', 'PUT', { expectedVersion: memory?.version || 0,
            reviewedSummary: $('memory-summary').value,
            details: { passportReadiness: +$('memory-passport').value, preferredWork: $('memory-work').value,
                mainObjection: $('memory-objection').value, nextStep: $('memory-next-step').value, visitPreference: $('memory-visit').value } });
        if (chat?.id !== selected) return;
        memory = saved;
        $('engine-status').textContent = 'تم حفظ ذاكرة العميل التي راجعها الموظف.'; await loadDrafts();
    }));
    $('generate-draft').addEventListener('click', () => guard($('generate-draft'), async () => {
        if (!chat?.control) throw new Error('عيّن المحادثة للمساعد أولاً.');
        $('engine-status').textContent = 'جاري إعداد المسودة…';
        await api('conversations/' + chat.id + '/drafts', 'POST', { expectedVersion: chat.control.ownershipVersion });
        await loadDrafts(); $('engine-status').textContent = 'راجع حالة المسودة ومصادرها قبل استخدامها.';
    }));
    $('knowledge-form').addEventListener('submit', e => { e.preventDefault(); const button = $('knowledge-form').querySelector('button'); guard(button, async () => {
        await api('knowledge', 'POST', { whatsAppAccountId: $('account').value, kind: +$('knowledge-kind').value,
            title: $('knowledge-title').value, content: $('knowledge-content').value, sourceReference: $('knowledge-source').value,
            validFrom: new Date().toISOString(), validUntil: new Date($('knowledge-until').value + 'T23:59:59').toISOString() });
        $('knowledge-status').textContent = 'أُضيفت للمراجعة. تحقق من المصدر ثم اعتمدها.'; await loadKnowledge();
    }, 'knowledge-status'); });
    $('refresh-improvements').addEventListener('click', () => guard($('refresh-improvements'), loadImprovements, 'improvement-status'));
    $('improvement-form').addEventListener('submit', e => { e.preventDefault(); const button = $('improvement-form').querySelector('button'); guard(button, async () => {
        if (!improvementSource || improvementSource.account !== $('account').value) throw new Error('اختار حالة من القائمة أولاً.');
        const source = improvementSource;
        await api('improvements', 'POST', { sourceDraftId: source.row.draftId, expectedSourceFingerprint: source.row.sourceFingerprint,
            title: $('improvement-title').value, exampleQuestion: $('improvement-question').value,
            proposedApproach: $('improvement-approach').value, reason: $('improvement-reason').value, privacyReviewed: $('improvement-privacy').checked });
        if ($('account').value !== source.account) return;
        resetImprovement(); $('improvement-status').textContent = 'حُفظ الاقتراح للمراجعة. لم يُعتمد بعد ولم يتغير رد العميل.';
        await loadKnowledge();
    }, 'improvement-status'); });
}());
