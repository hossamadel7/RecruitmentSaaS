(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const account = () => el('training-account').value;
    let config = null;
    const knowledgeStates=['للمراجعة','معتمدة','معطلة'];
    const analysisStates=['بانتظار التحليل','جارٍ التحليل','اكتمل التحليل','تعذر التحليل','تغير المصدر'];
    const outcomes={UNKNOWN:'النتيجة غير معروفة',CRM_CONVERSION:'تحويل مؤيد بسجل CRM',VERIFIED_VISIT:'زيارة مؤيدة بسجل استقبال',CRM_RECORDED_LOST:'خسارة مسجلة في CRM'};
    async function api(path, body, method) {
        const response = await fetch('/api/ai-sales-training/' + path, {
            method: method || (body ? 'POST' : 'GET'), credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value },
            body: body ? JSON.stringify(body) : undefined
        });
        const text = await response.text();
        const result = text ? JSON.parse(text) : null;
        if (!response.ok) throw new Error(result?.error || 'تعذر إتمام الطلب');
        return result;
    }
    function node(tag, text, className) { const n = document.createElement(tag); if (text != null) n.textContent = text; if (className) n.className = className; return n; }
    function guarded(fn) { return async event => { event?.preventDefault(); el('training-error').classList.add('d-none');
        const button = event?.currentTarget; if (button?.tagName === 'BUTTON') button.disabled = true;
        try { await fn(event); } catch (error) { el('training-error').textContent = error.message; el('training-error').classList.remove('d-none'); }
        finally { if (button?.tagName === 'BUTTON') button.disabled = false; }
    }; }
    function list(id, rows) { el(id).replaceChildren(...rows.map(r => node('li', `${r.text} (${r.count})`))); }
    async function refresh() {
        if (!account()) return;
        const d = await api('dashboard?accountId=' + account()); config = d.settings;
        el('training-open').checked = !!config?.includeOpenHistory;
        el('training-status').textContent = config?.paused === false ? 'التعلم نشط للحساب؛ النشر يحتاج مراجعة' : 'التعلم متوقف مؤقتًا';
        const metrics = [['المحادثات',d.totalConversations],['تم تحليلها',d.processed],['بانتظار التحليل',d.pending],['تحويلات CRM مؤيدة بسجل',d.verifiedConversions],['نتيجة غير معروفة',d.unknownOutcomes],['معرفة للمراجعة',d.pendingKnowledge],['معرفة معتمدة سارية',d.approvedKnowledge],['مداخل مفهرسة',d.indexed],['تدخلات بشرية',d.takeovers],['وحدات الإدخال اليوم',d.inputTokens],['وحدات الإخراج اليوم',d.outputTokens],['تكلفة تقديرية حسب الإعدادات',d.costRatesConfigured ? d.estimatedCost : 'أدخل أسعار الاستخدام لحسابها']];
        el('training-metrics').replaceChildren(...metrics.map(([label,value]) => { const n=node('div',null,'col-md-3 col-6'); n.append(node('div',label,'text-muted small'),node('strong',String(value))); return n; }));
        list('training-questions',d.questions); list('training-objections',d.objections);
        const knowledge=await api('knowledge?accountId='+account());
        el('training-knowledge').replaceChildren(...knowledge.map(({knowledge:k,index}) => {
            const card=node('article',null,'border rounded p-2 mb-2'); card.append(node('h3',k.title,'h6'));
            const trace=node('details',null,'small text-muted');trace.append(node('summary','المصدر وتفاصيل التتبع'),node('p',k.sourceReference));card.append(trace);
            card.append(node('p',`الحالة: ${knowledgeStates[k.status] || 'غير معروفة'} — ${k.kind===0?'معلومة عمل':'أسلوب محادثة'} — ثقة الاستخراج الآلية: ${index?.confidence ?? 'غير محددة'}`,'small'));
            const title=node('input',null,'form-control mb-2'); title.value=k.title; title.maxLength=150;
            const content=node('textarea',null,'form-control mb-2'); content.value=k.content; content.maxLength=4000; content.rows=3;
            const expiry=node('input',null,'form-control mb-2'); expiry.type='date'; expiry.value=k.validUntil.slice(0,10);
            const privacy=node('input'); privacy.type='checkbox'; const label=node('label',' راجعت المصدر الحالي والخصوصية '); label.prepend(privacy);
            if (k.status===0) { card.append(title,content,expiry,label);
                for (const [approved,caption] of [[true,'اعتماد المحتوى المصحح'],[false,'رفض']]) { const button=node('button',caption,'btn btn-sm btn-outline-primary me-2');
                    button.addEventListener('click',guarded(async()=>{await api(`knowledge/${k.id}/review`,{expectedVersion:k.version,approve:approved,title:title.value,content:content.value,validUntil:new Date(expiry.value+'T23:59:00Z').toISOString(),privacyReviewed:privacy.checked});await refresh();})); card.append(button); }
            } else { card.append(node('p',k.content)); if(k.status===1){const b=node('button','تعطيل هذه المعلومة','btn btn-sm btn-outline-danger');b.addEventListener('click',guarded(async()=>{await api(`knowledge/${k.id}/disable`,{expectedVersion:k.version});await refresh();}));card.append(b);} }
            return card;
        }));
        const analyses=await api('analyses?accountId='+account());
        el('training-analyses').replaceChildren(...analyses.map(a=>{const row=node('details',null,'border-bottom p-2');row.append(node('summary',`${a.conversationName || 'محادثة عميل'} — ${analysisStates[a.state] || 'غير معروف'} — ${outcomes[a.verifiedOutcome] || a.verifiedOutcome}`));
            row.append(node('p',`المرحلة المستنتجة: ${a.inferredStage}`,'small'),node('p',a.errorCode,'text-danger small'));
            const insights=JSON.parse(a.insightsJson);if(Array.isArray(insights))for(const chunk of insights){for(const [field,label]of [['questions','أسئلة العميل'],['objections','الاعتراضات'],['nextActions','خطوات مقترحة للمراجعة']])if(chunk[field]?.length){row.append(node('h4',label,'h6'));const ul=node('ul');ul.append(...chunk[field].map(text=>node('li',text)));row.append(ul);}}
            const trace=node('details',null,'small');trace.append(node('summary','الأدلة والتفاصيل'),node('p',a.outcomeEvidence),node('pre',JSON.stringify(insights,null,2),'text-wrap'));row.append(trace);
            if (a.state===0 || a.state===3 || a.state===1) {const retry=a.state===3 && a.attempts>=5;const b=node('button',retry?'إعادة المحاولة بعد إصلاح السبب':'تحليل هذه المحادثة','btn btn-sm btn-outline-primary');b.addEventListener('click',guarded(async()=>{await api(`analyses/${a.id}/${retry?'retry':'process'}`,retry?{expectedVersion:a.version}:{});await refresh();}));row.append(b);}
            if (a.state===2 && a.isEvaluationHoldout) {const b=node('button','إنشاء حالة من محادثة التقييم المحجوزة','btn btn-sm btn-outline-secondary');b.addEventListener('click',()=>{el('case-analysis').value=a.id;el('case-source').textContent=`حالة من محادثة تقييم محجوزة: ${a.conversationName || 'عميل'}`;el('case-question').required=false;el('case-scenario').focus();});row.append(b);} return row;}));
        const drafts=await api('drafts?accountId='+account());const placeholder=node('option','اختر ردًا لتقييمه');placeholder.value='';el('feedback-draft').replaceChildren(placeholder,...drafts.map(d=>{const o=node('option',d.reply.slice(0,90)||'رد لم يكتمل');o.value=d.id;return o;}));
        el('training-drafts').replaceChildren(...drafts.map(d=>{const row=node('details',null,'border-bottom p-2');row.append(node('summary',d.reply.slice(0,80)||'رد لم يكتمل'),node('p',d.reply));const b=node('button','تقييم هذا الرد','btn btn-sm btn-outline-secondary');b.addEventListener('click',()=>{el('feedback-draft').value=d.id;el('feedback-note').focus();});row.append(b);return row;}));
        const performance=await api('performance?accountId='+account());el('training-performance').replaceChildren(...performance.cohorts.map(c=>{const row=node('details',null,'small mb-2');row.append(node('summary',`${c.salespersonName || 'متحدث غير محدد'} — محادثات ${c.observed} | تحويل ${c.conversions} | زيارة ${c.visits} | خسارة مسجلة ${c.recordedLost} | غير معروف ${c.unknown}`),node('p',`مجموعة المصدر والدولة: ${c.key.cohort}`));return row;}));
    }
    el('training-refresh').addEventListener('click',guarded(refresh));
    el('training-account').addEventListener('change',guarded(refresh));
    for (const [id,paused] of [['training-pause',true],['training-resume',false]]) el(id).addEventListener('click',guarded(async()=>{await api('configure',{accountId:account(),paused,includeOpenHistory:el('training-open').checked,expectedVersion:config?.version||0});await refresh();}));
    el('training-scan').addEventListener('click',guarded(async()=>{await api('scan?accountId='+account(),{});await refresh();}));
    el('training-index').addEventListener('click',guarded(async()=>{await api('index?accountId='+account(),{});await refresh();}));
    el('training-official').addEventListener('submit',guarded(async()=>{await api('official',{accountId:account(),category:Number(el('official-category').value),title:el('official-title').value,content:el('official-content').value,sourceReference:el('official-source').value,validUntil:new Date(el('official-expiry').value+'T23:59:00Z').toISOString(),privacyReviewed:el('official-privacy').checked});el('training-official').reset();await refresh();}));
    el('training-search').addEventListener('submit',guarded(async()=>{const rows=await api(`search?accountId=${account()}&query=${encodeURIComponent(el('training-query').value)}`);el('training-matches').replaceChildren(...rows.map(r=>node('p',`${r.knowledge.title}: ${r.reason}; ${r.semanticScore ?? '—'}`)));}));
    el('training-feedback').addEventListener('submit',guarded(async()=>{await api('feedback',{id:crypto.randomUUID(),draftId:el('feedback-draft').value,kind:Number(el('feedback-kind').value),note:el('feedback-note').value,editedReply:el('feedback-edit').value});el('training-feedback').reset();await refresh();}));
    el('case-clear').addEventListener('click',()=>{el('case-analysis').value='';el('case-source').textContent='حالة افتراضية جديدة';el('case-question').required=true;});
    el('training-case').addEventListener('submit',guarded(async()=>{const historical=!!el('case-analysis').value;await api(historical?'evaluations/from-analysis':'evaluations/quick-case',{accountId:account(),analysisId:el('case-analysis').value||undefined,dataset:el('case-dataset').value,scenario:el('case-scenario').value,question:el('case-question').value,expected:el('case-expected').value,privacyReviewed:el('case-privacy').checked});el('training-case').reset();el('case-analysis').value='';el('case-question').required=true;el('training-status').textContent='تمت إضافة الحالة إلى المجموعة.';}));
    el('training-run').addEventListener('submit',guarded(async()=>{const r=await api('evaluations/run',{accountId:account(),dataset:el('run-dataset').value,configuration:el('run-config').value,baseline:el('run-baseline').value});el('training-status').textContent=`اكتمل تشغيل ${r.cases} حالة دون إرسال للعميل. راجع النتائج من المقارنة.`;}));
    el('training-compare').addEventListener('submit',guarded(async()=>{const query=new URLSearchParams({accountId:account(),dataset:el('evaluation-dataset').value,baseline:el('evaluation-baseline').value,candidate:el('evaluation-candidate').value});const r=await api('evaluations/compare?'+query);el('training-comparison').textContent=JSON.stringify({totalCases:r.totalCases,scoredPairs:r.scoredPairs,baselinePassed:r.baselinePassed,candidatePassed:r.candidatePassed,fullyEvaluated:r.fullyEvaluated},null,2);
        el('training-results').replaceChildren(...r.results.map(result=>{const card=node('article',null,'border rounded p-2 mb-2');card.append(node('h3',result.configuration,'h6'),node('p',result.reply),node('p',`المراجعة: ${result.passed===null?'لم تُراجع':result.passed?'اجتاز':'يحتاج تصحيح'}`));
            const review=node('textarea',null,'form-control mb-2');review.maxLength=2000;review.placeholder='راجع صحة الحقائق، اللغة، السياق، الاعتراض والتصعيد';card.append(review);
            for(const [passed,label]of [[true,'اجتاز المراجعة'],[false,'يحتاج تصحيح']]){const b=node('button',label,'btn btn-sm btn-outline-primary me-2');b.addEventListener('click',guarded(async()=>{await api(`evaluations/${result.id}/score`,{passed,review:review.value});b.textContent='تم حفظ التقييم';}));card.append(b);}return card;}));
    }));
    guarded(async()=>{const s=await api('status');if(!s.enabled){el('training-status').textContent='ميزة التعلم غير مفعلة في إعدادات النظام.';document.querySelectorAll('main button, main input, main textarea, main select').forEach(x=>x.disabled=true);return;}
        const response=await fetch('/api/ai-sales/setup',{credentials:'same-origin'});if(!response.ok)throw new Error('تعذر تحميل الحسابات');
        const accounts=(await response.json()).filter(a=>s.approvedAccountIds.includes(a.id));el('training-account').replaceChildren(...accounts.map(a=>{const o=node('option',a.name);o.value=a.id;return o;}));await refresh();})();
})();
