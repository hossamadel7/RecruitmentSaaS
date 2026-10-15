"""Prepare local, unapproved playbook candidates. No network, provider upload or training job."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import re

ALLOWED_ACCOUNTS = {
    "48af32a2-e7ce-440f-8a10-9b4f5d0dd6ad",
    "d8b3958f-bf45-4261-b93d-320169d137a3",
    "f7af14ad-a879-4eb4-a755-2b3602c4e3c5",
}
TOPICS = {
    "contract": r"مدة العقد|مده العقد|العقد.*سن|عقد.*مد[ته]",
    "hours": r"ساعات|ساعة.*شغل|ساعه.*شغل|ايام.*شغل|أيام.*شغل",
    "language": r"لغة|لغه|اتكلم تركي",
    "experience": r"خبرة|خبره",
    "driving": r"سواق|سائق",
    "pay": r"مرتب|راتب|الأجر|الاجر|دولار",
    "fees": r"تكلف|بتكلف|بدفع|رسوم|سعر|فلوس|مصاريف",
    "documents": r"جواز|باسبور|صورة.*بطاق|ورق|اوراق|أوراق|مستند",
    "office": r"عنوان|مكان المكتب|مواعيد المكتب|مواعيد العمل|يوم الجمع|من غير.*معاد|من غير.*ميعاد",
    "timing": r"امتى.*سفر|إمتى.*سفر|امتي.*سفر|السفر.*وقت|اسبوع|أسبوع",
    "roles": r"وظائف|مهن|شغل.*متاح|فرص.*شغل",
}


def normalized(text):
    text = re.sub(r"[\u064b-\u065f\u0670\u0640]", "", text)
    text = text.translate(str.maketrans("أإآىة", "ااايه"))
    return re.sub(r"[^\w]+", " ", text).strip().casefold()


def proposed_answer(topic, question):
    """Editorial drafts from owner-confirmed knowledge; not approved historical answers."""
    if topic == "pay":
        return "المرتب في عروض تركيا الحالية من 650 لـ850 دولار أمريكي شهريًا بعد الخصومات يا فندم، والسكن والأكل مشمولين بدون تكلفة إضافية.", ["turkey_pay_and_work"]
    if topic == "fees":
        return "المسؤول في المكتب يا فندم هيوضح لحضرتك التفاصيل لما تروح المكتب.", ["fees_and_unspecified_terms"]
    if topic == "documents":
        return "لو معاك جواز سفر، هات الجواز ومعاه صورة شخصية بخلفية بيضاء أو بطاقة شخصية؛ مش لازم الاتنين. ولو لسه ما طلعتش جواز، تقدر تزور المكتب بصورة البطاقة الشخصية.", ["documents_and_timing"]
    if topic == "office":
        if re.search("الجمع", question):
            return "المكتب مقفول يوم الجمعة يا فندم. تقدر تشرفنا من السبت للخميس من 9 الصبح لـ6 مساءً.", ["office_visit"]
        if re.search("عنوان|مكان", question):
            return "عنواننا محافظة الجيزة، 103 مكرر شارع الهرم، محطة الكوم الأخضر، بجوار فندق جوهرة الأهرام، أمام مجمع المدارس، مكتبة زمزم.", ["office_visit"]
        return "تقدر تشرفنا من السبت للخميس من 9 الصبح لـ6 مساءً، والحضور من غير موعد متاح. ولو تحب نرتب زيارة، أنهي يوم يناسبك؟", ["office_visit"]
    if topic == "hours":
        return "الشغل 9 ساعات في اليوم و6 أيام في الأسبوع يا فندم. مسؤول المكتب هيوضح لحضرتك باقي تفاصيل العقد.", ["turkey_pay_and_work", "fees_and_unspecified_terms"]
    if topic == "contract":
        return "مدة العقد لفرص تركيا الحالية سنتين يا فندم. باقي بنود العقد هيوضحها مسؤول المكتب.", ["documents_and_timing", "fees_and_unspecified_terms"]
    if topic == "timing":
        return "المدة المعتادة من التقديم للسفر حوالي أسبوع يا فندم، ودي مدة تقديرية مش موعد سفر مضمون.", ["documents_and_timing"]
    if topic == "language":
        return "معرفة اللغة التركية مش شرط للتقديم على فرصنا الحالية يا فندم.", ["turkey_roles_and_age"]
    if topic == "experience":
        return "الخبرة السابقة مش شرط عام للتقديم يا فندم. مسؤول المكتب هيراجع مع حضرتك تفاصيل الوظيفة.", ["turkey_roles_and_age"]
    if topic == "driving":
        return "شغل السائق مش متاح ضمن عروض تركيا الحالية يا فندم. تحب أعرفك على الوظائف المتاحة؟", ["turkey_roles_and_age"]
    return "متاح عندنا في عروض تركيا الحالية عمال مصانع وإنتاج وتعبئة وتغليف ونظافة، ومهن صناعية وأعمال زراعة. مسؤول المكتب هيشرح لحضرتك تفاصيل الوظيفة.", ["turkey_roles_and_age"]


def prepare(source_dir, workspace, output):
    source_dir, workspace, output = map(lambda p: Path(p).resolve(), (source_dir, workspace, output))
    if output == workspace or workspace in output.parents:
        raise ValueError("Customer-derived review artifacts must remain outside the repository.")
    if output.exists() and any(output.iterdir()):
        raise ValueError("Use a new empty output directory to preserve prior owner reviews.")
    corpus = source_dir / "crm-chat-review.jsonl"
    source_manifest = json.loads((source_dir / "manifest.json").read_text(encoding="utf-8-sig"))
    digest = hashlib.sha256(corpus.read_bytes()).hexdigest().upper()
    if digest != source_manifest["DataSha256"]:
        raise ValueError("Corpus fingerprint changed; review the source before preparing examples.")
    pack_path = workspace / "docs/knowledge/turkey-owner-knowledge.json"
    pack = json.loads(pack_path.read_text(encoding="utf-8-sig"))
    fact_keys = {e["key"] for e in pack["entries"] if e["kind"] == 0}
    chats = collections.defaultdict(list)
    source_rows = 0
    for line in corpus.open(encoding="utf-8-sig"):
        row = json.loads(line)
        source_rows += 1
        if row["accountId"] not in ALLOWED_ACCOUNTS:
            raise ValueError("Unexpected account in selected corpus.")
        chats[row["conversationToken"]].append(row)
    pool = []
    for token, messages in chats.items():
        combined = " ".join(m.get("reviewText") or "" for m in messages)
        if not re.search("تركيا|تركيه", combined) or re.search("السعودية|السعوديه|سعودي", combined):
            continue
        messages.sort(key=lambda m: (m["createdAtUtc"], m["internalMessageReference"]))
        for i, row in enumerate(messages):
            question = (row.get("reviewText") or "").strip()
            if row["speaker"] != "customer" or not row["candidateForReview"] or row["messageType"] != "1":
                continue
            if not 6 <= len(question) <= 200 or re.search(r"\[|https?://|@|\d{5,}", question):
                continue
            topic = next((topic for topic, rx in TOPICS.items() if re.search(rx, question)), None)
            if topic is None:
                continue
            nearby = next((m for m in messages[i + 1:i + 7] if m["speaker"] == "staff" and m["candidateForReview"] and m["messageType"] == "1"), None)
            if nearby is None:
                continue
            answer, keys = proposed_answer(topic, question)
            if not set(keys) <= fact_keys:
                raise ValueError("Proposed reply references absent owner facts.")
            prior_reply = any(m["speaker"] in ("staff", "ai_or_system", "staff_or_device") for m in messages[:i])
            pool.append({
                "topic": topic, "conversationToken": token, "accountId": row["accountId"],
                "questionReference": row["internalMessageReference"], "customerQuestion": question,
                "nearbyStaffReference": nearby["internalMessageReference"],
                "historicalStaffExcerpt": (nearby.get("reviewText") or "")[:350],
                "proposedReply": answer, "approvedFactKeys": keys,
                "ongoingConversation": prior_reply,
                "priorContextExcerpts": [{"speaker": m["speaker"], "text": (m.get("reviewText") or "")[:200]}
                                         for m in messages[max(0, i - 3):i] if m["messageType"] == "1"],
                "normalizedQuestion": normalized(question),
            })
    # Stable ordering without assuming historical success. Never mix the same conversation across splits.
    pool.sort(key=lambda r: hashlib.sha256(("playbook-review-v1:" + r["questionReference"]).encode()).hexdigest())
    buckets = {topic: [r for r in pool if r["topic"] == topic] for topic in TOPICS}
    used_chats, used_questions = set(), set()
    selected = {"playbook": [], "holdout": []}

    def take(topic, split):
        for row in buckets[topic]:
            if row["conversationToken"] not in used_chats and row["normalizedQuestion"] not in used_questions:
                used_chats.add(row["conversationToken"])
                used_questions.add(row["normalizedQuestion"])
                selected[split].append(row.copy())
                return True
        return False

    # Reserve a test example per topic where possible, preserving scarce contract examples for the playbook.
    for topic in TOPICS:
        if topic != "contract" and len(selected["holdout"]) < 10:
            take(topic, "holdout")
    for split, target in (("holdout", 10), ("playbook", 50)):
        while len(selected[split]) < target:
            before = len(selected[split])
            for topic in TOPICS:
                if len(selected[split]) < target:
                    take(topic, split)
            if len(selected[split]) == before:
                break
    if len(selected["playbook"]) != 50 or len(selected["holdout"]) != 10:
        raise ValueError("Not enough distinct eligible conversations/questions for the requested 50+10 split.")
    output.mkdir(parents=True, exist_ok=True)
    for split, rows in selected.items():
        for n, row in enumerate(rows, 1):
            row.pop("normalizedQuestion")
            row.update(candidateId=f"{split.upper()}-{n:03d}", reviewStatus="pending_owner_and_privacy_review",
                       privacyReviewed=False, responseApproved=False, trainingReady=False,
                       marketClassification="Turkey-keyword-only; owner review required",
                       replyProvenance="Editorial draft from owner-confirmed facts; not historical response approval",
                       pairingCaveat="Nearby staff response is supporting context, not proven to answer this message",
                       introductionPolicy="Greet/introduce only at conversation start; answer ongoing chats directly")
        with (output / f"{split}-candidates.jsonl").open("w", encoding="utf-8") as handle:
            for row in rows:
                handle.write(json.dumps(row, ensure_ascii=False) + "\n")
    manifest = {
        "sourceRows": source_rows, "sourceSha256": digest,
        "ownerKnowledgeSha256": hashlib.sha256(pack_path.read_bytes()).hexdigest().upper(),
        "eligiblePairedPool": len(pool), "playbookCandidates": 50, "heldoutCandidates": 10,
        "testAccountExcluded": True, "sameConversationInBothSplits": False,
        "exactNormalizedQuestionOverlap": False,
        "privacyStatus": "Partial masking only; manual privacy review required before any upload",
        "samePersonAcrossAccountsOrNearDuplicateLeakage": "Not established; manual review required",
        "approvedCandidates": 0, "providerUploadPerformed": False, "fineTuningJobCreated": False,
        "phase": "Historical learning: initial sales playbook review; fine-tuning deferred",
        "topicCounts": {split: dict(collections.Counter(r["topic"] for r in rows)) for split, rows in selected.items()},
        "ongoingCounts": {split: sum(r["ongoingConversation"] for r in rows) for split, rows in selected.items()},
    }
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    first = selected["playbook"][0]
    (output / "first-review.md").write_text(
        f"# Playbook candidate {first['candidateId']} — pending owner review\n\n"
        "Local customer-derived text may still contain personal information. Do not upload.\n\n"
        f"**Applicant question:** {first['customerQuestion']}\n\n"
        f"**Proposed corrected reply:** {first['proposedReply']}\n\n"
        "The reply is an editorial proposal using owner-confirmed facts, not an approved historical reply.\n",
        encoding="utf-8")
    print(json.dumps({"playbookCandidates": 50, "heldoutCandidates": 10, "approved": 0,
                      "topicCounts": manifest["topicCounts"], "ongoingCounts": manifest["ongoingCounts"],
                      "output": str(output)}, ensure_ascii=True))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_directory")
    parser.add_argument("workspace")
    parser.add_argument("output_directory")
    args = parser.parse_args()
    prepare(args.source_directory, args.workspace, args.output_directory)
