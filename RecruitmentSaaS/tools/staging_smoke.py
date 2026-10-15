"""Run inside the private network namespace; creates synthetic users only in validation_smoke."""
import html.parser
import http.cookiejar
import json
import os
import secrets
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

stage = Path(sys.argv[1]).resolve()
if not str(stage).startswith("/home/ubuntu/.local/share/recruitmentsaas-staging/"):
    raise SystemExit("Wrong staging directory")
if os.readlink("/proc/self/ns/net") == os.readlink("/proc/1/ns/net"):
    raise SystemExit("Network isolation required")
psql = ["/usr/lib/postgresql/16/bin/psql", "-h", "127.0.0.1", "-p", "55441", "-U", "ubuntu", "-d", "validation_smoke", "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1"]
branch = str(uuid.uuid4())
accounts = [{"id": str(uuid.uuid4()), "role": role, "email": f"smoke-{role}-{uuid.uuid4().hex}@example.test", "password": secrets.token_urlsafe(32)} for role in (1, 2, 7, 8)]
sql = f'''INSERT INTO demorecruitment."Branches" ("Id","Name","IsActive","CreatedAt") VALUES ('{branch}','Synthetic staging office {branch[:8]}',true,now());'''
for account in accounts:
    sql += f'''INSERT INTO demorecruitment."Users" ("Id","BranchId","FullName","Email","PasswordHash","Role","IsActive","CreatedAt","BaseSalary") VALUES ('{account['id']}','{branch}','Synthetic staging user','{account['email']}','{account['password']}',{account['role']},true,now(),0);'''
result = subprocess.run(psql, input=sql, text=True, capture_output=True)
if result.returncode:
    (stage / "seed-error.log").write_text(result.stderr)
    raise SystemExit("Synthetic staging seed failed; private log retained")

class TokenParser(html.parser.HTMLParser):
    token = None
    def handle_starttag(self, tag, attrs):
        data = dict(attrs)
        if tag == "input" and data.get("name") == "__RequestVerificationToken":
            self.token = data.get("value")

def request(opener, path, data=None):
    try:
        with opener.open("http://127.0.0.1:5191" + path, data=data, timeout=30) as response:
            return response.status, response.geturl(), response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as error:
        return error.code, error.geturl(), ""

checks = []
anonymous = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), urllib.request.ProxyHandler({}))
status, url, _ = request(anonymous, "/Admin")
checks.append(dict(name="anonymous-admin-redirect", passed=status == 200 and "/Auth/Login" in url))
status, _, _ = request(anonymous, "/Auth/Login", b"Email=x&Password=x")
checks.append(dict(name="login-CSRF-enforced", passed=status == 400))
routes = {1: ["/Admin", "/Inbox", "/Reservations", "/AiSalesAdmin", "/AiChatLearning", "/api/conversations"],
          2: ["/Reception"], 7: ["/TeleSalesManager", "/Reservations"], 8: ["/TeleSales", "/TeleSales/Leads"]}
for account in accounts:
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), urllib.request.ProxyHandler({}))
    status, _, body = request(opener, "/Auth/Login")
    parser = TokenParser(); parser.feed(body)
    if not parser.token:
        checks.append(dict(name=f"role-{account['role']}-login-token", passed=False)); continue
    payload = urllib.parse.urlencode({"Email": account["email"], "Password": account["password"], "__RequestVerificationToken": parser.token}).encode()
    status, url, _ = request(opener, "/Auth/Login", payload)
    checks.append(dict(name=f"role-{account['role']}-login", passed=status == 200 and "/Auth/Login" not in url, status=status))
    for route in routes[account["role"]]:
        status, url, _ = request(opener, route)
        checks.append(dict(name=f"role-{account['role']}:{route}", passed=status == 200 and "/Auth/" not in url, status=status))
    if account["role"] == 1:
        status, _, body = request(opener, "/api/ai-sales-operations")
        snapshot = json.loads(body) if status == 200 else {}
        checks.append(dict(name="AI-readiness-remains-disabled-and-no-pending-migrations", status=status,
            passed=status == 200 and snapshot.get("pendingMigrations") == [] and snapshot.get("livePilotQualified") is False and snapshot.get("automaticDispatchAvailable") is False))
    if account["role"] != 1:
        for route in ("/AiSalesAdmin", "/AiChatLearning"):
            status, url, _ = request(opener, route)
            checks.append(dict(name=f"role-{account['role']}-denied:{route}", passed=status == 403 or "/Auth/AccessDenied" in url, status=status))
report = dict(checkedOn="2026-10-14", fixtureUsers=len(accounts), fixturesWrittenOnlyTo="validation_smoke", networkIsolation=True,
              noModelRequests=True, checks=checks, allPassed=all(check["passed"] for check in checks), limitations="HTTP smoke tests, not browser layout or full write-workflow acceptance")
(stage / "smoke-results.json").write_text(json.dumps(report, indent=2))
print(json.dumps({"checks": len(checks), "allPassed": report["allPassed"], "failures": [check for check in checks if not check["passed"]]}))
raise SystemExit(0 if report["allPassed"] else 1)
