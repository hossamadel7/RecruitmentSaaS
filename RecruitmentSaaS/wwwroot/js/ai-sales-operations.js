(function () {
    'use strict';
    const $ = id => document.getElementById(id);
    function p(text) { const el = document.createElement('p'); el.textContent = text; $('operations-metrics').append(el); }
    const states = { pass: 'اجتاز الفحص المحدود', configured: 'الإعداد موجود؛ يحتاج تحققًا عمليًا', disabled: 'معطل أو غير مكتمل', review_required: 'تحتاج مراجعة وأدلة', attention: 'تحتاج تدخلًا', unavailable: 'غير متاح' };
    const gates = { Foundation: 'أساس التحكم', DatabaseMigrations: 'سجل ترحيل قاعدة البيانات', DraftProvider: 'مزود المسودات', VoiceProvider: 'مزود التفريغ',
        KnowledgeReview: 'دقة بيانات الشركة وتغطيتها', OfficePolicies: 'ساعات المكتب', QueueRecovery: 'استعادة المهام المتوقفة', RealModelEvaluation: 'تقييم الردود بعينات معتمدة',
        PrivacyAndRetention: 'الخصوصية والاحتفاظ والحذف', OperationalAcceptance: 'حدود الوقت والتكلفة وتجربة الأعطال والحمل', AutomaticRelease: 'الإرسال والتوزيع الآلي' };
    const statusNames = { Generating: 'جاري الإعداد', ReviewRequired: 'تحتاج مراجعة', Reviewed: 'راجعها الموظف', Rejected: 'مرفوضة', Stale: 'تغيّر السياق', Failed: 'تعذر الإعداد',
        Pending: 'في الانتظار', Processing: 'جاري التفريغ', ReadyForReview: 'تحتاج مراجعة', Confirmed: 'محفوظة', Cancelled: 'ملغاة', Visited: 'حضور موثق' };
    async function load() {
        $('refresh-operations').disabled = true;
        try {
            const response = await fetch('/api/ai-sales-operations'); const data = await response.json();
            if (!response.ok) throw new Error('تعذر قراءة بيانات التشغيل؛ راجع الاتصال بقاعدة البيانات والترحيلات.');
            $('operations-status').textContent = 'لم تثبت جاهزية تجربة حقيقية بعد. الإرسال والتوزيع الآلي غير متاحين. آخر تحديث: ' + new Date(data.generatedAt).toLocaleString('ar-EG');
            $('operations-metrics').replaceChildren(); $('operations-gates').replaceChildren();
            p('النطاق: هذا المستأجر فقط. الحالات الحالية لسجلات آخر 24 ساعة؛ المحاولات اليومية محسوبة بتوقيت UTC.');
            [['المسودات', data.drafts], ['التسجيلات', data.voiceTurns], ['الاستشارات', data.bookings]].forEach(([label, rows]) => p(label + ': ' + (rows.map(r => (statusNames[r.status] || r.status) + ' ' + r.count).join('، ') || 'لا توجد سجلات')));
            p('مسودات المسار المحلي: ' + data.localPolicyDrafts + '؛ سجلات استخدام مسودات غير مُبلّغ عنها أو لم تكتمل: ' + data.unreportedDraftUsage);
            p('رموز المسودات المُبلّغ عنها فقط: ' + data.reportedDraftInputTokens + ' إدخال، ' + data.reportedDraftOutputTokens + ' إخراج.');
            p('محاولات تفريغ اليوم: ' + data.voiceAttemptsToday + '؛ محاولات بلا استخدام مُبلّغ: ' + data.voiceAttemptsTodayWithoutReportedUsage + '.');
            p('استخدام آخر نتيجة محفوظة لكل تسجيل: ' + data.reportedVoiceInputTokens + ' رموز إدخال، ' + data.reportedVoiceOutputTokens + ' إخراج، ' + data.reportedVoiceSeconds + ' ثانية مُبلّغ عنها. الأرقام ليست مجموع تكاليف كل إعادة محاولة.');
            p('سجلات تفريغ لها بيانات رموز: ' + data.voiceRecordsWithReportedTokens + '؛ لها بيانات مدة: ' + data.voiceRecordsWithReportedSeconds + '؛ بلا بيانات استخدام: ' + data.unreportedVoiceUsage + '. إجمالي القيم المُبلّغ عنها لا يحوّل البيانات الناقصة إلى صفر.');
            p('المسودات المتوقفة: ' + data.interruptedDrafts + '؛ التسجيلات المتأخرة: ' + data.overdueVoiceTurns + '؛ تسجيلات معتمدة بلا مؤشر احتمالية: ' + data.reviewedVoiceWithoutProbability);
            p('حضور موثق: ' + data.verifiedVisits + '؛ حجوزات محفوظة بلا دليل حضور: ' + data.confirmedBookingsWithoutAttendanceEvidence + ' — لا تعني عدم الحضور.');
            const latency = data.draftLatency;
            p('مدة المسودة الكلية p50: ' + (latency.p50Milliseconds === null ? 'غير متاحة' : Math.round(latency.p50Milliseconds) + ' مللي ثانية') + '؛ p95: ' +
                (latency.p95Milliseconds === null ? 'غير متاحة' : Math.round(latency.p95Milliseconds) + ' مللي ثانية') + '؛ عينات: ' + latency.samples + (latency.truncated ? ' (آخر 1000 نتيجة فقط)' : '') + '. تشمل المسار المحلي والأخطاء، وليست قياسًا مستقلًا لوقت النموذج.');
            p('التكلفة المالية غير محسوبة؛ الاستخدام غير المُبلّغ يبقى مجهولًا.');
            if (data.pendingMigrations.length) p('الترحيلات غير المثبت تطبيقها في السجل: ' + data.pendingMigrations.join('، '));
            data.gates.forEach(g => { const li = document.createElement('li'); li.textContent = (gates[g.code] || g.code) + ': ' + (states[g.state] || g.state); $('operations-gates').append(li); });
        } catch (error) { $('operations-status').textContent = error.message; }
        finally { $('refresh-operations').disabled = false; }
    }
    $('refresh-operations').addEventListener('click', load);
    load();
})();
