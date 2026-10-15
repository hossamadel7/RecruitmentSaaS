"""Private, aggregate-only production data checks for the explicitly approved installation."""
import json
import subprocess
import sys
from pathlib import Path

mode, root_text = sys.argv[1:]
root = Path(root_text).resolve()
if mode not in ("before", "after") or not str(root).startswith("/home/ubuntu/.local/share/recruitmentsaas-installations/"):
    raise SystemExit("Invalid deployment verification arguments")
baseline = "20261011231817_AllowLeadStatusInTurkey"
latest = "20261013114423_AiStructuredCustomerMemory"
psql = ["runuser", "-u", "postgres", "--", "/usr/lib/postgresql/16/bin/psql", "-X", "-q", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-d", "RecruitmentCRM"]

def query(sql):
    result = subprocess.run(psql, input="SET TIME ZONE 'Africa/Cairo';\n" + sql, text=True, encoding="utf-8", capture_output=True)
    if result.returncode:
        (root / ("data-check-" + mode + ".log")).write_text(result.stderr)
        raise SystemExit("Private database verification failed")
    payload = result.stdout.strip()
    values = []
    decoder = json.JSONDecoder()
    while payload:
        value, length = decoder.raw_decode(payload)
        values.append(value)
        payload = payload[length:].lstrip()
    return values

def ident(value):
    return '"' + value.replace('"', '""') + '"'

if mode == "before":
    metadata = query('''SELECT json_agg(row_to_json(x)) FROM (
      SELECT n.nspname schema, c.relname name, json_agg(a.attname ORDER BY a.attnum) columns
      FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_attribute a ON a.attrelid=c.oid
      WHERE c.relkind IN ('r','p') AND n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema' AND a.attnum>0 AND NOT a.attisdropped
      GROUP BY n.nspname,c.relname ORDER BY n.nspname,c.relname) x;''')[0]
else:
    metadata = json.loads((root / "data-before.json").read_text())["tables"]

queries = []
for table in metadata:
    relation = ident(table["schema"]) + "." + ident(table["name"])
    projection = ",".join(ident(column) for column in table["columns"])
    where = f''' WHERE "MigrationId" <= '{baseline}' ''' if table["name"] == "__EFMigrationsHistory" else ""
    literal = relation.replace("'", "''")
    queries.append(f'''SELECT json_build_object('relation','{literal}','rows',(SELECT count(*) FROM {relation}{where}),
      'digest',(SELECT md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM
      (SELECT md5(row_to_json(r)::text) h FROM (SELECT {projection} FROM {relation}{where}) r) hashes));''')
rows = query("\n".join(queries))
history = query('''SELECT json_build_object('latest',max("MigrationId"),'newRows',count(*) FILTER (WHERE "MigrationId">'20261011231817_AllowLeadStatusInTurkey')) FROM public."__EFMigrationsHistory";''')[0]
if mode == "before":
    if history["latest"] != baseline:
        raise SystemExit("Production migration baseline changed")
    for table, row in zip(metadata, rows):
        table.update(row)
    (root / "data-before.json").write_text(json.dumps(dict(tables=metadata, migration=history), indent=2))
    print(json.dumps(dict(originalTables=len(rows), baselineVerified=True)))
else:
    expected = {table["relation"]: table for table in metadata}
    failures = [row["relation"] for row in rows if row["rows"] != expected[row["relation"]]["rows"] or row["digest"] != expected[row["relation"]]["digest"]]
    passed = not failures and len(rows) == len(metadata) and history["latest"] == latest and history["newRows"] == 7
    report = dict(tablesCompared=len(rows), allOriginalRowsPreserved=not failures, migration=history, passed=passed, mismatches=failures)
    (root / "data-after.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(dict(tablesCompared=len(rows), passed=passed)))
    if not passed:
        raise SystemExit(1)
