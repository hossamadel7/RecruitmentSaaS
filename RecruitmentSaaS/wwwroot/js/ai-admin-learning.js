(() => {
    'use strict';
    const el = id => document.getElementById(id), account = () => el('admin-account').value;
    let channel;
    const states = ['في قائمة الانتظار','بانتظار المدير','إجابة للمراجعة','إجابة معتمدة','مسودة جاهزة للمراجعة','يحتاج متابعة بشرية','ملغى'];
    function node(tag, text, cls) { const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n; }
    async function api(path, body) { const response=await fetch('/api/ai-admin-learning/'+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json','RequestVerificationToken':document.querySelector('input[name="__RequestVerificationToken"]').value},body:body?JSON.stringify(body):undefined});const t=await response.text(),r=t?JSON.parse(t):null;if(!response.ok)throw new Error(r?.error||'تعذر إتمام الطلب');return r; }
    function guard(fn) { return async e=>{e?.preventDefault();el('admin-error').classList.add('d-none');const b=e?.currentTarget;if(b)b.disabled=true;try{await fn(e);}catch(error){el('admin-error').textContent=error.message;el('admin-error').classList.remove('d-none');}finally{if(b)b.disabled=false;}}; }
    function button(text, fn) { const b=node('button',text,'btn btn-sm btn-outline-primary me-2 mb-2');b.type='button';b.addEventListener('click',guard(fn));return b; }
    async function refresh() {
        if(!account())return;
        const d=await api('status?accountId='+account());channel=d.channel;
        el('admin-status').textContent=!d.routingEnabled?'قناة المدير غير مفعلة بعد؛ يلزم تطبيق التحديث وإعداد الحساب أولًا.':!d.enabled||!d.accountApproved?'تعلم المدير متوقف لهذا الحساب؛ المحادثات الخاصة تظل منفصلة.':!channel?'اربط رقم المدير وابدأ التوثيق.':!channel.verifiedAt?'بانتظار إرسال رمز التوثيق من رقم المدير.':`${channel.paused?'التدريب متوقف':'القناة موثقة والتدريب نشط'} — ${channel.phone}${d.outboundApproved?'':' — إرسال واتساب للمدير غير مفعل بعد'}`;
        el('admin-bind').hidden=!!channel;el('admin-bind').classList.toggle('d-none',!!channel);el('admin-renew').hidden=!channel||!!channel.verifiedAt;
        el('admin-requests').replaceChildren(...d.requests.map(r=>{
            const card=node('article',null,'border rounded p-3 mb-3');card.append(node('h3',`${r.code} — ${states[r.state]}`,'h6'),node('p',r.question));
            const link=node('a','فتح المحادثة','d-block mb-2');link.href='/Inbox?conversationId='+encodeURIComponent(r.conversationId);card.append(link);
            if(r.errorCode)card.append(node('p',r.errorCode,'text-warning'));
            if(r.state<=2){
                const answer=node('textarea',null,'form-control mb-2');answer.value=r.answer;answer.maxLength=3000;answer.rows=3;answer.setAttribute('aria-label','الإجابة المصححة');
                const scope=node('select',null,'form-select mb-2');scope.setAttribute('aria-label','نطاق الإجابة');for(const [v,t]of [[1,'لهذا العميل فقط'],[2,'قاعدة عامة'],[3,'معلومة مؤقتة'],[4,'تصحيح معلومة قديمة']]){const o=node('option',t);o.value=v;scope.append(o);}
                const expiry=node('input',null,'form-control mb-2');expiry.type='date';expiry.value=new Date(Date.now()+7*86400000).toISOString().slice(0,10);expiry.setAttribute('aria-label','انتهاء صلاحية الإجابة');
                const publish=node('input');publish.type='checkbox';const label=node('label',' راجعت المعلومة وأوافق على نشرها كمعرفة عامة ');label.prepend(publish);
                const title=node('input',null,'form-control mb-2');title.placeholder='عنوان المعلومة عند النشر';title.maxLength=150;
                const replaces=node('input',null,'form-control mb-2');replaces.placeholder='معرف المعلومة القديمة عند التصحيح (اختياري)';
                card.append(answer,scope,expiry,title,label,replaces,button('اعتماد الإجابة بعد المراجعة',async()=>{await api(`requests/${r.id}/validate`,{expectedVersion:r.version,answer:answer.value,scope:Number(scope.value),validUntil:new Date(expiry.value+'T23:59:00Z').toISOString(),publish:publish.checked,title:title.value,replacesKnowledgeId:replaces.value||null});await refresh();}));
            }else if(r.answer)card.append(node('p',r.answer));
            if(r.state===3)card.append(button('تجهيز مسودة العميل للمراجعة',async()=>{await api(`requests/${r.id}/prepare`,{});await refresh();}));
            if(r.replyDraftId){const review=node('a','مراجعة مسودة الرد','btn btn-outline-success btn-sm me-2');review.href='/AiSalesAdmin?conversationId='+encodeURIComponent(r.conversationId);card.append(review);}
            if(r.state!==6)card.append(button('إلغاء المتابعة الآلية لهذا السؤال',async()=>{await api(`requests/${r.id}/cancel`,{expectedVersion:r.version});await refresh();}));
            return card;
        }));
        el('admin-messages').replaceChildren(...d.messages.map(m=>node('p',`${m.incoming?'المدير':'المساعد'}: ${m.text} (${m.state})`,'border-bottom pb-2 text-break')));
    }
    el('admin-refresh').onclick=guard(refresh);el('admin-account').onchange=guard(async()=>{el('admin-code').textContent='';await refresh();});
    el('admin-bind').onsubmit=guard(async()=>{const r=await api('bind',{accountId:account(),phone:el('admin-phone').value});el('admin-code').textContent='أرسل هذا الرمز من رقمك إلى رقم الشركة خلال 15 دقيقة: '+r.verificationCode;await refresh();});
    el('admin-renew').onclick=guard(async()=>{const r=await api('renew-verification',{accountId:account()});el('admin-code').textContent='أرسل هذا الرمز من رقمك خلال 15 دقيقة: '+r.verificationCode;});
    for(const [id,paused]of [['admin-pause',true],['admin-resume',false]])el(id).onclick=guard(async()=>{if(!channel)throw new Error('اربط رقم المدير أولًا');await api('pause',{accountId:account(),expectedVersion:channel.version,paused});await refresh();});
    el('admin-pump').onclick=guard(async()=>{await api('pump',{accountId:account()});await refresh();});
    el('admin-ask').onsubmit=guard(async()=>{await api('ask',{conversationId:el('admin-conversation').value,question:el('admin-question').value||null});await refresh();});
    el('admin-conversation').value=new URLSearchParams(location.search).get('conversationId')||'';
    guard(async()=>{const response=await fetch('/api/ai-sales/setup');if(!response.ok)throw new Error('تعذر تحميل حسابات واتساب');const rows=await response.json();el('admin-account').replaceChildren(...rows.map(a=>{const o=node('option',a.name);o.value=a.id;return o;}));await refresh();})();
})();
