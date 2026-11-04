"""Read selected CRM history through SSH and prepare a private, local sales review.

No model calls, training uploads, knowledge publication or customer sends.
"""
import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess

ACCOUNTS = {
    "48af32a2-e7ce-440f-8a10-9b4f5d0dd6ad": "Bakar",
    "d8b3958f-bf45-4261-b93d-320169d137a3": "Mekky",
    "f7af14ad-a879-4eb4-a755-2b3602c4e3c5": "Sherif",
}
TOPICS = {
    "fees": r"تكلف|مصاريف|رسوم|فلوس|سعر|غالي|غالى|بدفع|بكام",
    "salary": r"مرتب|راتب|دولار|الاجر|اجور",
    "documents": r"جواز|باسبور|اوراق|مستند|بطاقه|بطاقة",
    "office_visit": r"عنوان|مكان المكتب|مواعيد|ازاي اجي|ازاى اجى|هجيل|هاجيل|اجي امتي",
    "eligibility": r"سني|سنى|عمري|عمرى|سن |سنه|سنة|جنسيه|جنسية",
    "roles": r"وظيف|وظايف|وظائف|سواق|سائق|مهن|شغل ايه|نوع الشغل",
    "timing": r"امتي|امتى|اسبوع|السفر ام|موعد السفر|وقت السفر",
    "trust": r"مضمون|ضمان|نصب|ترخيص|مرخص|مصداق|حقيقي|حقيقى",
    "hesitation": r"هفكر|حافكر|هشوف|متردد|بفكر|افكر",
    "contract": r"عقد|اقامه|اقامة|تاشير|تامين",
    "conditions": r"ساعات|ساعه|اجازه|اجازة|\bسكن\b|\bالسكن\b|\bاكل\b|\bالاكل\b|وجبات",
    "experience_language": r"خبره|خبرة|لغه|لغة|اتكلم تركي",
    "human_request": r"اكلم حد|اكلم موظف|اكلم مسؤول|بني ادم|بنى ادم|انت روبوت",
    "opt_out": r"مش مهتم|غير مهتم|متبعتش|ماتبعتش|ما تبعتش|وقف الرسايل|لا تراسل",
}


def normalize(value):
    value = re.sub(r"[\u064b-\u065f\u0670\u0640]", "", value)
    return value.translate(str.maketrans("أإآىة", "ااايه" )).casefold()


def current_offers(host, destination):
    """Read active catalogue records and approved knowledge; active is not owner verification."""
    ids = ",".join("'" + value + "'" for value in ACCOUNTS)
    query = '''SELECT json_build_object(
      'packages',(SELECT coalesce(json_agg(json_build_object('id',"Id",'name',"Name",
        'country',"DestinationCountry",'job',"JobTitle",'priceEgp',"PriceEGP",
        'description',"Description")),'[]') FROM demorecruitment."JobPackages" WHERE "IsActive"),
      'companyJobs',(SELECT coalesce(json_agg(json_build_object('id',j."Id",'country',c."Country",
        'job',j."JobTitle",'requested',j."RequestedCount")),'[]')
        FROM demorecruitment."CompanyJobs" j JOIN demorecruitment."Companies" c ON c."Id"=j."CompanyId"
        WHERE j."IsActive" AND c."IsActive"),
      'knowledge',(SELECT coalesce(json_agg(json_build_object('id',"Id",'account',"WhatsAppAccountId",
        'kind',"Kind",'title',"Title",'content',"Content",'validUntil',"ValidUntil",
        'currentlyValid',"ValidFrom"<=now() AND "ValidUntil">now())),'[]')
        FROM demorecruitment."AiSalesKnowledge" WHERE "Status"=1 AND "WhatsAppAccountId" IN (IDS)));'''
    query = query.replace("IDS", ids)
    remote = '''import subprocess,os
r=subprocess.run(['runuser','-u','postgres','--','psql','-X','-qAt','-v','ON_ERROR_STOP=1',
 '-d','RecruitmentCRM'],input=QUERY,text=True,capture_output=True,
 env=os.environ|{'PGOPTIONS':'-c default_transaction_read_only=on -c statement_timeout=15000'})
if r.returncode: raise SystemExit('Read-only offer check failed')
print(r.stdout)
'''.replace("QUERY", repr(query))
    result = subprocess.run(["ssh", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes",
                             "-o", "ConnectTimeout=15", host, "sudo -n python3 -"],
                            input=remote, text=True, encoding="utf-8", capture_output=True, timeout=40)
    if result.returncode:
        raise RuntimeError("Read-only offer check failed; no catalogue accepted.")
    data = json.loads(result.stdout)
    destination.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def snapshot(host, destination):
    # Raw names/text are masked on the server before leaving the database host.
    # The single SELECT has a consistent PostgreSQL snapshot, including old/closed chats.
    query = '''SELECT json_build_object(
      'id',m."Id",'chat',m."ConversationId",'account',m."WhatsAppAccountId",
      'direction',m."Direction",'type',m."MessageType",'source',m."MessageSource",
      'status',m."Status",'ai',m."SenderAiSalesAgentId" IS NOT NULL,
      'staff',m."SenderUserId" IS NOT NULL,'at',m."CreatedAt",'text',m."TextBody",
      'names',ARRAY[c."Name",l."FullName",u."FullName"])
      FROM demorecruitment."WhatsAppMessages" m
      JOIN demorecruitment."WhatsAppConversations" w ON w."Id"=m."ConversationId"
      LEFT JOIN demorecruitment."WhatsAppContacts" c ON c."Id"=w."ContactId"
      LEFT JOIN demorecruitment."Leads" l ON l."Id"=w."LeadId"
      LEFT JOIN demorecruitment."Users" u ON u."Id"=m."SenderUserId"
      WHERE m."WhatsAppAccountId" IN (ACCOUNT_IDS)
        AND w."WhatsAppAccountId"=m."WhatsAppAccountId"
      ORDER BY m."ConversationId",m."CreatedAt",m."Id";'''
    query = query.replace("ACCOUNT_IDS", ",".join("'" + x + "'" for x in ACCOUNTS))
    remote = '''import subprocess,json,re,hashlib,hmac,secrets,sys
query=QUERY
result=subprocess.run(['runuser','-u','postgres','--','psql','-X','-qAt',
 '-v','ON_ERROR_STOP=1','-d','RecruitmentCRM'],input=query,text=True,
 capture_output=True,env=__import__('os').environ | {'PGOPTIONS':
 '-c default_transaction_read_only=on -c statement_timeout=120000 -c lock_timeout=3000'})
if result.returncode: raise SystemExit('Read-only snapshot failed; no data exported')
key=secrets.token_bytes(32)
for line in result.stdout.splitlines():
 row=json.loads(line)
 names=row.pop('names')
 text=row.pop('text') or ''
 text=re.sub(r'https?://[^\\s<>]+','[link removed]',text,flags=re.I)
 text=re.sub(r'[\\w.+-]+@[\\w.-]+\\.[A-Za-z]{2,}','[email removed]',text)
 text=re.sub(r'(?<!\\d)(?:\\+?\\d[\\s()-]*){7,}(?!\\d)','[identifier removed]',text)
 parts={p for n in names if n for p in [n]+n.split() if len(p)>=3}
 for name in sorted(parts,key=len,reverse=True):
  text=re.sub(r'(?<![^\\W\\d_])'+re.escape(name)+r'(?![^\\W\\d_])','[name removed]',text,flags=re.I)
 row['chat']=hmac.new(key,row['chat'].encode(),hashlib.sha256).hexdigest()
 row['text']=text if row['type']==1 else None
 print(json.dumps(row,ensure_ascii=False))
'''.replace("QUERY", repr(query))
    result = subprocess.run(["ssh", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes",
                             "-o", "ConnectTimeout=15", host, "sudo -n python3 -"],
                            input=remote, text=True, encoding="utf-8", capture_output=True, timeout=180)
    if result.returncode:
        raise RuntimeError("Read-only snapshot failed. No corpus was accepted.")
    # Validate every row before writing; do not expose source text in command output.
    rows = [json.loads(line) for line in result.stdout.splitlines()]
    if not rows or any(r["account"] not in ACCOUNTS for r in rows):
        raise ValueError("Empty or incorrectly scoped snapshot.")
    destination.write_text(result.stdout, encoding="utf-8")


def analyse(source, output):
    chats = defaultdict(list)
    counts, topics, topic_chats, examples = Counter(), Counter(), defaultdict(set), defaultdict(list)
    accounts = defaultdict(Counter)
    oldest, newest = None, None
    for line in source.open(encoding="utf-8"):
        row = json.loads(line)
        if row["account"] not in ACCOUNTS:
            raise ValueError("Unexpected account in corpus.")
        chats[row["chat"]].append(row)
        counts["messages"] += 1
        counts["text" if row["type"] == 1 else "audio" if row["type"] == 4 else "other_media"] += 1
        accounts[ACCOUNTS[row["account"]]]["messages"] += 1
        oldest = min(oldest or row["at"], row["at"])
        newest = max(newest or row["at"], row["at"])
    seen_questions = defaultdict(set)
    for token, rows in chats.items():
        rows.sort(key=lambda r: (r["at"], r["id"]))
        accounts[ACCOUNTS[rows[0]["account"]]]["conversations"] += 1
        combined = normalize(" ".join(r["text"] or "" for r in rows))
        markets = [name for name, rx in {"Turkey": "تركيا|تركيه", "Saudi": "سعود",
                   "Armenia": "ارمينيا|ارمينيه", "Oman": r"\bعمان\b|\bعماني\b|\bعمانيه\b",
                   "Qatar": r"\bقطر\b", "Kuwait": "كويت", "UAE": "امارات|دبي|ابوظبي|ابو ظبي",
                   "Bahrain": "بحرين", "Jordan": "الاردن"}.items() if re.search(rx, combined)]
        counts["market_" + ("mixed" if len(markets) > 1 else markets[0] if markets else "unlabelled")] += 1
        for index, row in enumerate(rows):
            if row["direction"] != 1 or row["type"] != 1 or not row["text"]:
                continue
            question = normalize(row["text"])
            for topic, pattern in TOPICS.items():
                if not re.search(normalize(pattern), question):
                    continue
                topics[topic] += 1
                topic_chats[topic].add(token)
                # Keep full conversation context private; pairs are review leads, never asserted successes.
                if len(examples[topic]) >= 12 or question in seen_questions[topic]:
                    continue
                following = rows[index + 1:index + 7]
                reply = next((r for r in following if r["direction"] == 2 and r["type"] == 1
                              and not r["ai"] and r["source"] != 4 and r["status"] in (3, 4, 5)
                              and r["text"]), None)
                if reply is None:
                    continue
                seen_questions[topic].add(question)
                examples[topic].append({"conversation": token, "account": ACCOUNTS[row["account"]],
                    "marketsKeywordOnly": markets, "questionReference": row["id"],
                    "question": row["text"][:600], "replyReference": reply["id"],
                    "nearbyHumanReply": reply["text"][:900], "staffAuthorKnown": reply["staff"],
                    "context": [{"speaker": "customer" if r["direction"] == 1 else "outgoing",
                                 "text": r["text"][:400]} for r in rows[max(0,index-2):index] if r["text"]],
                    "approved": False, "pairing": "Nearby response; relevance requires review"})
    report = {"preparedAtUtc": datetime.now(timezone.utc).isoformat(), "conversations": len(chats),
              "counts": dict(counts), "accounts": dict(accounts), "firstMessage": oldest,
              "lastMessage": newest, "topicsKeywordOnly": [
                  {"topic": topic, "messages": count, "conversations": len(topic_chats[topic])}
                  for topic, count in topics.most_common()],
              "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
              "coverage": "Every stored message in the three selected accounts; text analysed locally. Audio/media not interpreted.",
              "limitations": ["Partial masking; private review required", "Keyword labels may overlap or misclassify",
                              "No conversion/success inference", "Historical claims are not current business facts"],
              "providerUploadPerformed": False, "modelTrained": False, "knowledgePublished": False}
    (output / "analysis.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (output / "review-examples.json").write_text(json.dumps(examples, ensure_ascii=False, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="ubuntu@158.69.206.138")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output).resolve()
    workspace = Path(__file__).resolve().parents[1]
    if output == workspace or workspace in output.parents:
        raise ValueError("Private customer review must stay outside the repository.")
    output.mkdir(parents=True, exist_ok=False)
    source = output / "masked-history.jsonl"
    snapshot(args.host, source)
    report = analyse(source, output)
    current_offers(args.host, output / "current-offers.json")
    print(json.dumps({"conversations": report["conversations"], "counts": report["counts"],
                      "accounts": report["accounts"], "output": str(output)}, ensure_ascii=True))
