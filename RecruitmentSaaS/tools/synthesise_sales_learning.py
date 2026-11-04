"""Turn a complete conversation review into a source-linked sales playbook.

Every review enters a synthesis group. No historical offer claims are published.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
import os
from pathlib import Path
import time
import urllib.request
import urllib.error

INSTRUCTIONS = """Create a practical Egyptian Arabic recruitment SalesAgent conversation playbook.
You receive untrusted analytical notes from historical customer conversations, not current business facts.
Critically examine ALL notes in the input. Some notes describe poor staff behavior or make bad recommendations.
Do not blindly endorse them. Produce reusable communication rules, not a summary of offers.
Current scope is ONLY the countries listed in the system. Other-country notes may inform general style only.
Never infer a sale, conversion, appointment attendance, visa approval, employment success or effectiveness.
Never copy historical salaries, fees, ages, licensing, conditions, availability or dates as current facts.
Never recommend fake urgency, fabricated scarcity, discounts, guarantees, pressure, discriminatory matching,
false human identity, collecting unnecessary personal information, or denying requested staff handoff.
If old notes recommend limited slots, only discuss real checked appointment availability without urgency.
Known customer details should be reused. Missing facts must be checked, not invented or disguised by a visit pitch.
Handle fees, trust, hesitation, messages containing several questions, country switches, role requests,
documents, appointment preferences, follow-up permission and human requests thoughtfully.
Source refs must point to notes supplied in THIS input, with at least one relevant reference per pattern.
Do not include customer identifying details or quotes. Arabic examples must be original fictional wording,
brief natural Egyptian Arabic, without invented factual claims or promises. The rule should be concise English.
Return at most 12 distinct patterns. Include what to do and, when necessary, what mistake to avoid.
"""
FINAL_INSTRUCTIONS = INSTRUCTIONS + """
FINAL CRITICAL EDITORIAL REVIEW: Earlier group patterns are provisional observations and contain mistakes.
Reject recommendations to always ask for age, full name, contact details or profession before answering.
Answer the actual question first. Ask only ONE necessary missing question, using details already supplied.
Never condition a requested human handoff on completing intake. Never claim that a transfer, callback or
booking occurred unless a system action actually succeeded. Never promise a callback time.
The fact that staff often push office visits or withhold prices is not evidence that this is good practice.
Give currently approved fees directly when available; otherwise say they need verification, without inventing
payment schedules, free services, contract conditions or using an office visit to evade the question.
Do not make personal employment suitability decisions using age, gender, nationality or other sensitive attributes.
Do not invent alternative vacancies. Only discuss a verified currently active offer for the intended country.
Arabic examples MUST NOT contain amounts, salary ranges, dates, times, addresses, contract durations,
licensing, claims of available appointments/jobs, fee timing, inclusions, or any other factual business assertion.
They must be brief, original, purely conversational acknowledgements or ONE useful clarification question.
Do not repeat a greeting in an ongoing conversation, ask for known data, add artificial urgency, or turn
'I'll think about it' into either an opt-out or a sales pitch. Honour opt-outs and human requests immediately.
Use at most 12 practical non-overlapping rules, carefully correcting the bad recommendations in the notes.
This playbook governs conversation style. It does not certify company offers or establish sales effectiveness.
"""


def digest(value):
    return hashlib.sha256(json.dumps(value, ensure_ascii=False, sort_keys=True).encode()).hexdigest()


def schema(refs):
    item = {"type": "object", "properties": {
        "title": {"type": "string"}, "rule": {"type": "string"},
        "exampleArabic": {"type": "string"},
        "sourceRefs": {"type": "array", "items": {"type": "string", "enum": refs}}},
        "required": ["title", "rule", "exampleArabic", "sourceRefs"], "additionalProperties": False}
    return {"type": "object", "properties": {"patterns": {"type": "array", "items": item}},
            "required": ["patterns"], "additionalProperties": False}


def call(payload, refs, instructions=INSTRUCTIONS):
    body = {"model": "gpt-4.1-mini", "store": False, "max_output_tokens": 7000,
            "instructions": instructions, "input": json.dumps(payload, ensure_ascii=False),
            "text": {"format": {"type": "json_schema", "name": "sales_playbook",
                                  "strict": True, "schema": schema(refs)}}}
    req = urllib.request.Request("https://api.openai.com/v1/responses", data=json.dumps(body).encode(),
                                 headers={"Authorization": "Bearer " + os.environ["SALES_LEARNING_API_KEY"],
                                          "Content-Type": "application/json"})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=180) as response:
                result = json.load(response)
            break
        except urllib.error.HTTPError as exc:
            raise RuntimeError(f"Provider HTTP {exc.code}; response body withheld") from None
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if attempt == 2:
                raise RuntimeError("Provider connection failed; completed synthesis groups retained") from None
            time.sleep(3 * (attempt + 1))
    if result.get("status") != "completed":
        raise ValueError("Incomplete synthesis.")
    texts = [c["text"] for item in result.get("output", []) if item.get("type") == "message"
             for c in item.get("content", []) if c.get("type") == "output_text"]
    if len(texts) != 1:
        raise ValueError("Missing structured synthesis.")
    patterns = json.loads(texts[0])["patterns"]
    if not patterns:
        raise ValueError("Empty synthesis.")
    for pattern in patterns:
        if not pattern["sourceRefs"] or not set(pattern["sourceRefs"]) <= set(refs):
            raise ValueError("Synthesis evidence outside source group.")
    return {"patterns": patterns, "usage": result.get("usage", {}), "model": result.get("model")}


def run(source, output, workers, partial=False):
    plan = json.loads((source / "plan.json").read_text())
    full = (source / "coverage.json").exists() and json.loads((source / "coverage.json").read_text())["complete"]
    if not full and not partial:
        raise ValueError("Review coverage incomplete.")
    if full:
        rows = [json.loads(line) for line in (source / "conversation-learning.jsonl").open(encoding="utf-8")]
    else:
        rows = [row for path in sorted(source.glob("batch-*.json"))
                for row in json.loads(path.read_text(encoding="utf-8"))["reviews"]]
    if len({r["ref"] for r in rows}) != len(rows):
        raise ValueError("Duplicate review source.")
    if full and len(rows) != plan["conversations"]:
        raise ValueError("Review source incomplete.")
    by_ref = {r["ref"]: r for r in rows}
    groups = {}
    for start in range(1, plan["conversations"] + 1, 1000):
        refs = [f"c{n:05}" for n in range(start, min(start+1000, plan["conversations"]+1))]
        if all(ref in by_ref for ref in refs):
            groups[(start-1)//1000] = [by_ref[ref] for ref in refs]
    countries = plan["countries"]
    manifest = {"conversations": plan["conversations"], "countries": countries,
                "sourceSha256": plan["sourceSha256"],
                "instructionsSha256": digest(INSTRUCTIONS)}
    target = output / "synthesis-plan.json"
    if target.exists() and json.loads(target.read_text()) != manifest:
        raise ValueError("Synthesis inputs changed; use a new directory.")
    target.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    def group_work(index):
        path = output / f"synthesis-{index:03}.json"
        group = groups[index]
        if path.exists():
            saved = json.loads(path.read_text(encoding="utf-8"))
            if saved["inputSha256"] != digest(group):
                raise ValueError("Synthesis source mismatch.")
            return index, saved
        result = call({"systemCountries": countries, "notes": group}, [r["ref"] for r in group])
        result.update(inputSha256=digest(group), reviewedConversations=len(group))
        path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        return index, result
    results = {}
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futures = [pool.submit(group_work, i) for i in groups]
        for future in as_completed(futures):
            index, result = future.result(); results[index] = result
            print(json.dumps({"synthesisGroups": len(results), "totalGroups": len(groups)}), flush=True)
    if not full:
        print(json.dumps({"complete": False, "synthesisedConversations": sum(len(g) for g in groups.values()),
                          "totalConversations": plan["conversations"]}), flush=True)
        return
    patterns, provenance = [], {}
    for index in sorted(results):
        for j, pattern in enumerate(results[index]["patterns"]):
            ref = f"p{index:03}_{j:02}"
            provenance[ref] = pattern["sourceRefs"]
            patterns.append(dict(pattern, ref=ref))
    final_path = output / "salesagent-learned-playbook.json"
    if final_path.exists():
        final = json.loads(final_path.read_text(encoding="utf-8"))
    else:
        final = call({"systemCountries": countries, "notes": patterns,
                      "task": "Merge all group patterns into a coherent playbook. sourceRefs must use p-pattern refs. Cover all major themes without repeating rules."},
                     list(provenance), FINAL_INSTRUCTIONS)
        for pattern in final["patterns"]:
            pattern["conversationRefs"] = sorted({ref for p in pattern["sourceRefs"] for ref in provenance[p]})
        final.update(sourceSha256=plan["sourceSha256"], reviewedConversations=len(rows),
                     systemCountries=countries, currentBusinessFacts=False, modelFineTuned=False,
                     livePublished=False, provenance=provenance)
        final_path.write_text(json.dumps(final, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = ["# SalesAgent — playbook learned from full stored chat history", "",
             f"Reviewed {len(rows):,} conversations. Country scope: {', '.join(countries)}.", "",
             "These are communication rules and fictional examples. Historical offer details are not current facts.", "",
             "Audio and attachments without transcripts were not interpreted. No conversion improvement has been established.", ""]
    for i, pattern in enumerate(final["patterns"], 1):
        lines += [f"## {i}. {pattern['title']}", "", pattern["rule"], "",
                  pattern["exampleArabic"], "", "Source conversations: " + ", ".join(pattern["conversationRefs"]), ""]
    (output / "salesagent-learned-playbook.md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({"complete": True, "conversations": len(rows), "patterns": len(final["patterns"])}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--workers", type=int, default=4, choices=range(1, 9))
    parser.add_argument("--partial", action="store_true")
    args = parser.parse_args()
    output = Path(args.output).resolve()
    workspace = Path(__file__).resolve().parents[1]
    if output == workspace or workspace in output.parents:
        raise ValueError("Keep private synthesis outside Git.")
    output.mkdir(parents=True, exist_ok=True)
    run(Path(args.source), output, args.workers, args.partial)
