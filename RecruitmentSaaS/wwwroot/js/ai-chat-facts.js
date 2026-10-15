(function () {
    'use strict';
    const root = document.getElementById('chat-fact-review');
    const $ = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
    const titles = { salary: 'الراتب', fees: 'الرسوم والدفع', office: 'المكتب والزيارة', work: 'ساعات العمل والسكن', documents: 'الأوراق', timing: 'مدة الإجراءات', roles: 'الوظائف والخبرة', age: 'السن المعلن' };
    let cursor = null, snapshot = null, finished = false, question = null, busy = false;
    function status(text) { $('learning-status').textContent = text; }
    async function api(path, body) {
        const response = await fetch('/api/ai-chat-learning/' + path, { method: body === undefined ? 'GET' : 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token }, body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store' });
        const text = await response.text();
        let data; try { data = text ? JSON.parse(text) : null; } catch (_) { throw new Error('تعذر قراءة الرد. راجع تسجيل الدخول والاتصال.'); }
        if (!response.ok) throw new Error(data?.error || 'تعذر إتمام الطلب. راجع صلاحيتك والإعدادات.');
        return data;
    }
    async function run(action) {
        if (busy) return; busy = true;
        root.querySelectorAll('button, select').forEach(el => el.disabled = true);
        try { await action(); } catch (error) { status(error.message); }
        finally {
            busy = false; root.querySelectorAll('button, select').forEach(el => el.disabled = false);
            $('extract-chat-facts').disabled = !$('learning-account').value;
            $('extract-chat-facts').textContent = finished ? 'بدء جولة استخراج جديدة' : 'استخراج الدفعة التالية';
            $('next-chat-fact').disabled = !$('learning-account').value;
            $('publish-chat-fact').disabled = !question || !question.sourceUnchanged;
        }
    }
    async function next() {
        question = await api('facts/next?accountId=' + encodeURIComponent($('learning-account').value));
        $('learning-question').hidden = !question;
        if (!question) { status('لا توجد أسئلة معلقة للحساب. يمكنك استخراج دفعة أخرى.'); return; }
        const c = question.candidate;
        $('learning-topic').textContent = titles[c.topic] || c.topic;
        $('learning-source').textContent = c.sourceStatement;
        const speakers = { customer: 'العميل', staff: 'الموظف', staff_or_device: 'رسالة من الجهاز؛ الموظف غير محدد' };
        $('learning-source-info').textContent = 'مصدر الكلام: ' + (speakers[c.speaker] || c.speaker) + '؛ تاريخ الرسالة: ' +
            (question.sourceCreatedAt ? new Date(question.sourceCreatedAt).toLocaleString('ar-EG', {timeZone:'Africa/Cairo'}) : 'غير متاح') + '؛ مرجع الرسالة: ' + (c.sourceMessageId || 'محذوف');
        $('learning-title').value = titles[c.topic] || c.topic;
        $('learning-corrected').value = ''; $('learning-privacy').checked = false;
        $('learning-related').replaceChildren();
        if (!question.relatedFacts.length) { const p = document.createElement('p'); p.textContent = 'لا توجد معلومات مرتبطة معتمدة حاليًا.'; $('learning-related').append(p); }
        question.relatedFacts.forEach(f => {
            const div = document.createElement('div'); div.className = 'border rounded p-2 mb-2';
            const p = document.createElement('p'); p.textContent = f.title + ': ' + f.content;
            const select = document.createElement('select'); select.className = 'form-select'; select.dataset.fact = f.id;
            [['', 'اختر قرارًا صريحًا'], ['keep', 'احتفظ بالمعلومة الحالية'], ['retire', 'أوقف المعلومة الحالية']].forEach(([value, label]) => { const o = document.createElement('option'); o.value = value; o.textContent = label; select.append(o); });
            div.append(p, select); $('learning-related').append(div);
        });
        status(question.sourceUnchanged ? 'راجع الكلام واكتب المعلومة الصحيحة، أو ارفض الاقتراح.' : 'المصدر تغيّر أو حُذف. ارفض الاقتراح وأعد الاستخراج؛ لا يمكن اعتماده.');
    }
    $('learning-account').addEventListener('change', () => run(async () => { cursor = snapshot = null; finished = false; $('learning-progress').textContent = ''; await next(); }));
    $('next-chat-fact').addEventListener('click', () => run(next));
    $('extract-chat-facts').addEventListener('click', () => run(async () => {
        if (finished) { cursor = snapshot = null; finished = false; }
        const data = await api('extract', { accountId: $('learning-account').value, afterMessageId: cursor, snapshotBefore: snapshot, pageSize: 100 });
        cursor = data.page.nextAfterMessageId; snapshot = data.page.snapshotBefore; finished = !data.page.hasMore;
        $('learning-progress').textContent = 'تمت مراجعة ' + data.page.items.length + ' رسالة في هذه الدفعة، وإضافة ' + data.addedCandidates + ' اقتراحًا.' + (finished ? ' انتهت الدفعات المتاحة حتى تاريخ الاستخراج.' : ' توجد دفعات أخرى.');
        await next();
    }));
    async function decide(publish) {
        if (!question) return;
        const days = Number($('learning-days').value);
        if (publish && (!Number.isInteger(days) || days < 1 || days > 90 || !$('learning-privacy').checked || !$('learning-corrected').value.trim()))
            throw new Error('اكتب المعلومة الصحيحة، أكد مراجعتها وحدد مدة من 1 إلى 90 يومًا.');
        const resolutions = publish ? question.relatedFacts.map(f => {
            const decision = Array.from($('learning-related').querySelectorAll('select')).find(el => el.dataset.fact === f.id).value;
            if (!decision) throw new Error('اختر الاحتفاظ أو الإيقاف لكل معلومة مرتبطة.');
            return { knowledgeId: f.id, expectedVersion: f.version, retire: decision === 'retire' };
        }) : [];
        await api('facts/' + question.candidate.id + '/review', { expectedVersion: question.candidate.version, publish,
            confirmedTitle: $('learning-title').value, confirmedContent: $('learning-corrected').value, privacyReviewed: $('learning-privacy').checked,
            validUntil: new Date(Date.now() + (publish ? days : 7) * 86400000).toISOString(), resolutions });
        $('learning-decision-status').textContent = publish ? 'تم اعتماد المعلومة الصحيحة في معرفة المساعد.' : 'تم رفض الاقتراح؛ لم يُضف إلى معرفة المساعد.';
        await next();
    }
    $('publish-chat-fact').addEventListener('click', () => run(() => decide(true)));
    $('reject-chat-fact').addEventListener('click', () => run(() => decide(false)));
    run(async () => {
        const accounts = await api('accounts');
        accounts.forEach(a => { const o = document.createElement('option'); o.value = a.id; o.textContent = a.name; $('learning-account').append(o); });
        if (!accounts.length) { status('استخراج المحادثات معطل أو لم تُحدد حسابات لهذه المراجعة.'); return; }
        await next();
    });
})();
