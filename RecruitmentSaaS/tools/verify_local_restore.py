"""Verify a private pg_dump against the dedicated local rehearsal DB; no row text is printed."""
import argparse
import json
import re
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--pg-bin", type=Path, required=True)
parser.add_argument("--backup", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
psql = [str(args.pg_bin / "psql.exe"), "-h", "127.0.0.1", "-p", "55440", "-U", "rehearsal_owner", "-d", "recruitment_restore", "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1"]

def query(sql):
    result = subprocess.run(psql, input=sql, text=True, encoding="utf-8", capture_output=True)
    if result.returncode:
        raise RuntimeError("Local verification query failed; no database row data logged")
    return result.stdout.strip()

identifier = r'(?:"(?:[^"]|"")+"|[A-Za-z_][A-Za-z_0-9]*)'
copy_start = re.compile(rf'^COPY ({identifier}\.{identifier}) \(')
counts = {}
with args.output.with_suffix(".pg-restore.log").open("w", encoding="utf-8") as errors:
    process = subprocess.Popen([str(args.pg_bin / "pg_restore.exe"), "--data-only", "--file=-", str(args.backup)], stdout=subprocess.PIPE, stderr=errors, text=True, encoding="utf-8")
    current = None
    for line in process.stdout:
        if current is not None:
            if line.rstrip("\r\n") == r"\.":
                current = None
            else:
                counts[current] += 1
        else:
            match = copy_start.match(line)
            if match:
                current = match.group(1)
                if current in counts:
                    raise RuntimeError("Duplicate COPY relation in archive")
                counts[current] = 0
    if process.wait() or current is not None or not counts:
        raise RuntimeError("Archive COPY verification failed")

results = []
for relation, expected in counts.items():
    actual = int(query(f"SELECT count(*) FROM {relation};"))
    # regclass resolves the archive-generated, validated relation name. Literal quoting is separate.
    literal = "'" + relation.replace("'", "''") + "'"
    columns = json.loads(query(f"SELECT json_agg(attname ORDER BY attnum) FROM pg_attribute WHERE attrelid={literal}::regclass AND attnum>0 AND NOT attisdropped;"))
    projection = ",".join('"' + name.replace('"', '""') + '"' for name in columns)
    digest_sql = f"SELECT md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM (SELECT md5(row_to_json(r)::text) h FROM (SELECT {projection} FROM {relation}) r) hashes;"
    digest = query(digest_sql)
    results.append(dict(relation=relation, archiveRows=expected, restoredRows=actual, columns=columns, existingRowsDigest=digest, digestSql=digest_sql, countMatches=expected == actual))
report = dict(database="recruitment_restore", host="127.0.0.1", port=55440, verifiedTables=len(results), allCountsMatch=all(row["countMatches"] for row in results), digestPurpose="Detect accidental changes to original column values during isolated migration; not a cryptographic authenticity proof", tables=results)
args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({key: report[key] for key in ("verifiedTables", "allCountsMatch")}, ensure_ascii=False))
if not report["allCountsMatch"]:
    raise SystemExit(1)
